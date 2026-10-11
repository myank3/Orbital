using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;

namespace Orbital.Service;

public class CallService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public CallService(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<(bool Success, string? Error, CallSessionDto? Session)> InitiateCallAsync(
        string initiatorId,
        InitiateCallDto dto)
    {
        // 1-on-1 Call Validation: Ensure neither peer is on an ongoing call
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

    public IceServerConfigDto GetIceServers()
    {
        var stunServers = _config.GetSection("WebRtc:StunServers").Get<List<string>>()
            ?? new List<string> { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" };

        return new IceServerConfigDto(stunServers);
    }
}