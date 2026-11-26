namespace SiteSense.Api.Dtos;

public sealed record PositionBatchResponse(int Received, int Stored, int Duplicates, int OutsidePerimeter);
