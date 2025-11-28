namespace NatsHttpGateway.Models;

public class StreamSummary
{
    public string Name { get; set; } = string.Empty;
    public string[] Subjects { get; set; } = Array.Empty<string>();
    public ulong Messages { get; set; }
    public ulong Bytes { get; set; }
    public ulong FirstSeq { get; set; }
    public ulong LastSeq { get; set; }
    public int Consumers { get; set; }
}

public class StreamListResponse
{
    public int Count { get; set; }
    public List<StreamSummary> Streams { get; set; } = new();
}

public class SubjectDetail
{
    public string Subject { get; set; } = string.Empty;
    public ulong Messages { get; set; }
}

public class StreamSubjectsResponse
{
    public string StreamName { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<SubjectDetail> Subjects { get; set; } = new();
    public string? Note { get; set; }
}
