namespace KiwiAuth.Models.Requests;

public record ResetPasswordRequest(string UserId, string Token, string NewPassword);
