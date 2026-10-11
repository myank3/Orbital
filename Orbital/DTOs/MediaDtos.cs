namespace Orbital.DTOs;

public record MediaUploadResponseDto(
    string Url,
    string FileName,
    long FileSizeBytes,
    int MediaType
);