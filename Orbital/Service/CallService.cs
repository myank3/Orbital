using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Orbital.Service;

public class CallService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public CallService(
        ApplicationDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<(bool Success, string? Error, CallSessionDto? Session)> InitiateCallAsync(
        string initiatorId,
        InitiateCallDto dto)
    {
        // ──────────────────────────────────────────────────────────
        // Stale session sweep
        // ──────────────────────────────────────────────────────────
        var now = DateTime.UtcNow;
        var ringingCutoff = now.AddMinutes(-2);
        var activeCutoff = now.AddHours(-4);

        var staleSessions = await _db.CallSessions
            .Where(c => c.EndedAt == null &&
                        ((c.Status == CallStatus.Ringing && c.StartedAt < ringingCutoff) ||
                         (c.Status == CallStatus.Active && c.StartedAt < activeCutoff)))
            .ToListAsync();

        if (staleSessions.Count > 0)
        {
            foreach (var stale in staleSessions)
            {
                stale.EndedAt = now;
                stale.Status = CallStatus.Ended;
            }
            await _db.SaveChangesAsync();
        }

        // ──────────────────────────────────────────────────────────
        // 1-on-1 Call Validation
        // ──────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(dto.PeerId))
        {
            var isPeerBusy = await _db.CallSessions.AnyAsync(c =>
                (c.InitiatorId == dto.PeerId || c.PeerId == dto.PeerId ||
                 c.InitiatorId == initiatorId || c.PeerId == initiatorId) &&
                c.EndedAt == null &&
                c.Status != CallStatus.Ended &&
                c.Status != CallStatus.Rejected &&
                c.Status != CallStatus.Missed);

            if (isPeerBusy)
                return (false, "User is currently on another call.", null);
        }

        var session = new CallSession
        {
            Id = Guid.NewGuid(),
            Type = (CallType)dto.CallType,
            Status = CallStatus.Ringing,
            InitiatorId = initiatorId,
            PeerId = dto.PeerId,
            GroupId = dto.GroupId,
            StartedAt = DateTime.UtcNow
        };

        await _db.CallSessions.AddAsync(session);
        await _db.SaveChangesAsync();

        var sessionDto = new CallSessionDto(
            SessionId: session.Id,
            CallType: (int)session.Type,
            Status: (int)session.Status,
            InitiatorId: session.InitiatorId,
            PeerId: session.PeerId,
            GroupId: session.GroupId,
            StartedAt: session.StartedAt,
            EndedAt: session.EndedAt
        );

        return (true, null, sessionDto);
    }

    public async Task<bool> EndCallAsync(Guid sessionId)
    {
        var session = await _db.CallSessions.FirstOrDefaultAsync(c => c.Id == sessionId);

        if (session == null || session.EndedAt != null || session.Status == CallStatus.Ended)
            return false;

        session.EndedAt = DateTime.UtcNow;
        session.Status = CallStatus.Ended;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateCallStatusAsync(Guid sessionId, CallStatus status)
    {
        var session = await _db.CallSessions.FirstOrDefaultAsync(c => c.Id == sessionId);

        if (session == null || session.EndedAt != null)
            return false;

        session.Status = status;
        if (status == CallStatus.Ended || status == CallStatus.Rejected || status == CallStatus.Missed)
        {
            session.EndedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    // ─── GetIceServers ────────────────────────────────────────
    // Cloudflare is PRIMARY. Metered is FALLBACK only.
    //
    // If Cloudflare returns valid TURN credentials, only Cloudflare
    // is included in the response — Metered is not added. If Cloudflare
    // fails (network error, API error, or returns no TURN entries),
    // Metered is added as a fallback. If both fail, STUN-only.
    public async Task<List<IceServerConfigDto>> GetIceServersAsync()
    {
        var stunServers = _config.GetSection("WebRtc:StunServers").Get<List<string>>()
            ?? new List<string> { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" };

        var result = new List<IceServerConfigDto>
        {
            new(stunServers) // STUN has no credentials
        };

        // ── Cloudflare TURN (primary) ──────────────────────────
        var cloudflareAdded = false;
        var cfTokenId = _config["Cloudflare:TurnTokenId"];
        var cfApiToken = _config["Cloudflare:ApiToken"];

        if (!string.IsNullOrWhiteSpace(cfTokenId) && !string.IsNullOrWhiteSpace(cfApiToken))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var apiUrl = $"https://rtc.live.cloudflare.com/v1/turn/keys/{cfTokenId}/credentials/generate-ice-servers";

                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfApiToken);
                request.Content = new StringContent("{\"ttl\":86400}", Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var iceServersArray = doc.RootElement.GetProperty("iceServers");

                // Cloudflare returns iceServers as an ARRAY:
                //   [0] = STUN-only entry (no credentials)
                //   [1] = TURN entry with username/credential
                // Only add entries that have credentials (TURN, not STUN).
                foreach (var entry in iceServersArray.EnumerateArray())
                {
                    if (!entry.TryGetProperty("username", out var u) ||
                        !entry.TryGetProperty("credential", out var c))
                    {
                        continue; // STUN-only — skip
                    }

                    var urls = entry.GetProperty("urls").EnumerateArray()
                        .Select(x => x.GetString()!)
                        .Where(url => !url.Contains(":53")) // browsers block port 53
                        .ToList();

                    if (urls.Count == 0)
                        continue;

                    result.Add(new IceServerConfigDto(
                        urls,
                        u.GetString(),
                        c.GetString()
                    ));
                    cloudflareAdded = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CallService] Cloudflare TURN failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── Metered TURN (FALLBACK ONLY) ──────────────────────
        // Added only when Cloudflare did not provide any TURN entries.
        // If Cloudflare succeeded, Metered is not included at all.
        if (!cloudflareAdded)
        {
            var meteredUsername = _config["Metered:Username"];
            var meteredCredential = _config["Metered:Credential"];
            var meteredUrls = _config.GetSection("Metered:Urls").Get<List<string>>()
                ?? new List<string>();

            if (!string.IsNullOrWhiteSpace(meteredUsername) &&
                !string.IsNullOrWhiteSpace(meteredCredential) &&
                meteredUrls.Count > 0)
            {
                result.Add(new IceServerConfigDto(
                    meteredUrls,
                    meteredUsername,
                    meteredCredential
                ));
            }
        }

        return result;
    }
}