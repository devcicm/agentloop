using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AgentLoop.Terminals;


/// <summary>Veredicto de la politica sobre un comando concreto.</summary>
public sealed record PolicyDecision(bool Allowed, string Reason, bool Simulated = false);
