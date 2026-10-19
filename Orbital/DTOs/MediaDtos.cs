namespace Orbital.DTOs;

public record MediaUploadResponseDto(
    string Url,
    string FileName,
    long FileSizeBytes,
    int MediaType
);

public record PresignUploadRequestDto(
    string FileName,
    string ContentType,
    long SizeBytes,
    string Kind,          // "dm" | "group" | "avatar"
    int? GroupId          // required when Kind = "group"
);

public record PresignUploadResponseDto(
    string MediaKey,
    string UploadUrl,
    DateTime ExpiresAt
);

public record PresignDownloadRequestDto(
    List<string> Keys
);

public record PresignedMessagesResponse<T>(
    List<T> Messages,
    Dictionary<string, string> PresignedUrls
);