using Microsoft.AspNetCore.Components.Routing;

namespace DietPlanner.Components.Layout;

public sealed record NavItem(string Href, string Label, IconName Icon, NavLinkMatch Match = NavLinkMatch.Prefix)
{
    // Shared by the sidebar links and the mobile top bar's page title, so the
    // two can't drift apart.
    public static IReadOnlyList<NavItem> All { get; } =
    [
        new("", "Week Plan", IconName.Calendar, NavLinkMatch.All),
        new("meals", "Meals", IconName.Utensils),
        new("meals/upload", "Upload Meals", IconName.Upload),
        new("settings", "Settings", IconName.Settings),
    ];
}
