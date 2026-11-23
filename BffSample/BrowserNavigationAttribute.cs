namespace BffSample;

/// <summary>
/// Marks a BFF endpoint the browser reaches by navigating rather than by a fetch, so the request cannot carry the
/// <see cref="CsrfHeader"/> and the check for it lets the endpoint through.
/// </summary>
/// <remarks>
/// A page on another site can send the browser to such an endpoint at will, so it may carry the attribute only when
/// it changes nothing on the user's behalf, or acts only on an unguessable value that the SPA alone received.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BrowserNavigationAttribute : Attribute;
