namespace ArrDash.Services;

public readonly record struct ColorPreset(string Id, string Label, string Value);

public static class AppearanceColorPresets
{
    public static readonly ColorPreset[] Accent =
    [
        new("indigo", "Indigo", "#6366f1"),
        new("cyan", "Cyan", "#03a9f4"),
        new("blue", "Blue", "#2563eb"),
        new("teal", "Teal", "#14b8a6"),
        new("green", "Green", "#22c55e"),
        new("violet", "Violet", "#8b5cf6"),
        new("rose", "Rose", "#f43f5e"),
        new("amber", "Amber", "#f59e0b"),
    ];

    public static string DefaultAccent => Accent[0].Value;

    public static readonly ColorPreset[] LightText =
    [
        new("slate", "Slate", "#0f172a"),
        new("charcoal", "Charcoal", "#111827"),
        new("navy", "Navy", "#1e293b"),
        new("forest", "Forest", "#14532d"),
        new("wine", "Wine", "#581c87"),
        new("brown", "Brown", "#422006"),
        new("black", "Black", "#000000"),
    ];

    public static readonly ColorPreset[] DarkText =
    [
        new("white", "White", "#ffffff"),
        new("cloud", "Cloud", "#f1f5f9"),
        new("silver", "Silver", "#cbd5e1"),
        new("steel", "Steel", "#94a3b8"),
        new("cream", "Cream", "#fef3c7"),
        new("sky", "Sky", "#bfdbfe"),
        new("mint", "Mint", "#bbf7d0"),
    ];

    public static string DefaultLightText => LightText[0].Value;
    public static string DefaultDarkText => DarkText[1].Value;

    public static readonly ColorPreset[] LightBackground =
    [
        new("mist", "Mist", "#f4f6fb"),
        new("paper", "Paper", "#ffffff"),
        new("sand", "Sand", "#faf7f2"),
        new("cloud", "Cloud", "#eef2ff"),
        new("mint", "Mint", "#ecfdf5"),
    ];

    public static readonly ColorPreset[] DarkBackground =
    [
        new("midnight", "Midnight", "#0b1020"),
        new("slate", "Slate", "#111827"),
        new("navy", "Navy", "#0f172a"),
        new("charcoal", "Charcoal", "#171717"),
        new("plum", "Plum", "#1e1033"),
    ];

    public static string DefaultLightBackground => LightBackground[0].Value;
    public static string DefaultDarkBackground => DarkBackground[0].Value;
}
