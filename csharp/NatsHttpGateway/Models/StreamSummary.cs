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

    // Configuration details
    public StreamConfigurationInfo Configuration { get; set; } = new();

    // State details
    public StreamStateInfo State { get; set; } = new();
}

public class StreamConfigurationInfo
{
    public string? Description { get; set; }
    public int Replicas { get; set; }
    public string Storage { get; set; } = string.Empty;
    public string Retention { get; set; } = string.Empty;
    public string Discard { get; set; } = string.Empty;
    public bool NoAck { get; set; }
    public TimeSpan? MaxAge { get; set; }
    public long MaxBytes { get; set; }
    public long MaxMsgSize { get; set; }
    public long MaxMsgs { get; set; }
    public long MaxConsumers { get; set; }
    public long MaxMsgsPerSubject { get; set; }
    public TimeSpan? DuplicateWindow { get; set; }
    public bool AllowRollup { get; set; }
    public bool DenyDelete { get; set; }
    public bool DenyPurge { get; set; }
    public bool Sealed { get; set; }
}

public class StreamStateInfo
{
    public ulong Messages { get; set; }
    public ulong Bytes { get; set; }
    public ulong FirstSeq { get; set; }
    public ulong LastSeq { get; set; }
    public string? FirstTime { get; set; }
    public string? LastTime { get; set; }
    public int ConsumerCount { get; set; }
    public long NumSubjects { get; set; }
    public long NumDeleted { get; set; }
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
