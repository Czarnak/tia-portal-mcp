namespace TiaMcpServer.Contracts.Hmi;

/// <summary>One culture's text of a multilingual property, returned raw as Openness holds it.</summary>
public sealed record HmiText(string Culture, string Text);
