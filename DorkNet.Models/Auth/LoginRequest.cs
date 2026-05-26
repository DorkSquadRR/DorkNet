namespace DorkNet.Models.Auth;

public class LoginRequest
{
    public string? Name { get; set; }
    public long BuildTimestamp { get; set; }
    public int Platform { get; set; }
    public string? DeviceId { get; set; }
}
