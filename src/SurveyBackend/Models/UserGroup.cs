namespace SurveyBackend.Models;

/// <summary>
/// 用户身份组
/// </summary>
public enum UserGroup
{
    NewComer = 0,
    PendingUser = 1,
    VerifiedUser = 2,
    Admin = 99,
    SuperAdmin = 100
}
