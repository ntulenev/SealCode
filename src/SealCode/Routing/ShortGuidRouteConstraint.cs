using Models;

namespace SealCode.Routing;

/// <summary>
/// Route constraint that validates whether a route parameter
/// represents a valid <see cref="ShortGuid"/> value.
/// </summary>
/// <remarks>
/// This constraint is intended to be used in route templates, for example:
/// <code>
/// /room/{roomId:ShortGuid}
/// </code>
///
/// The constraint does not perform model binding or value conversion.
/// It only determines whether the route value can be parsed as a
/// <see cref="ShortGuid"/>.
/// </remarks>
#pragma warning disable CA1515 // Need for tests
public sealed class ShortGuidRouteConstraint : IRouteConstraint
#pragma warning restore CA1515 
{
    /// <summary>
    /// Determines whether the route parameter matches a valid <see cref="ShortGuid"/>.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="route">The router evaluating the constraint.</param>
    /// <param name="routeKey">The name of the route parameter.</param>
    /// <param name="values">The route values dictionary.</param>
    /// <param name="routeDirection">The routing direction.</param>
    /// <returns>
    /// <see langword="true"/> if the route value exists and can be parsed
    /// as a valid <see cref="ShortGuid"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(routeKey, out var value) || value is null)
        {
            return false;
        }

        var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

        return ShortGuid.TryParse(s, out var parsed) && parsed.Value != Guid.Empty;
    }
}
