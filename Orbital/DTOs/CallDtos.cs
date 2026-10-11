namespace Orbital.DTOs;

// ICE Servers configuration served by API
public record IceServerConfigDto(
    List<string> Urls,
    string? Username = null,
    string? Credential = null
);

// Call lifecycle events
public record InitiateCallDto(
    string? PeerId,         // Specified if 1-on-1 direct call
    int? GroupId,           // Specified if group voice channel
    int CallType            // 0: Audio, 1: Video
);

public record CallSessionDto(
    Guid SessionId,
    int CallType,
    int Status,
    string InitiatorId,
    string? PeerId,
    int? GroupId,
    DateTime StartedAt,
    DateTime? EndedAt
);