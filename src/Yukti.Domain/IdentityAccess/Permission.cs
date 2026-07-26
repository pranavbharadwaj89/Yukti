namespace Yukti.Domain.IdentityAccess;

/// <summary>
/// Closed enum, unlike ModuleKind — the set of possible permissions is a
/// fixed, core piece of the authorization model (Volume 1 Part IV §25.1).
/// </summary>
public enum Permission
{
    FlowCreate,
    FlowEdit,
    FlowPublish,
    FlowExecute,
    FlowView,
    ReportView,
    ModuleManage,
    UserManage,
}
