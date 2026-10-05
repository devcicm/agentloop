using System.Text.RegularExpressions;
namespace AgentLoop.Goals;


public sealed record OperationTaskContext(string Objective, string SessionContext, bool Inherited);
