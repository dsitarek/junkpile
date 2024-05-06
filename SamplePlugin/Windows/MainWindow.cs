using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace Junkpile.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin plugin;
    private readonly Junkpile junkpile;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin, Junkpile junk)
        : base("Junkpile", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        junkpile = junk;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (junkpile.JunkItemNames.Any())
        {
            junkpile.JunkItemNames.ForEach(item =>
            {
                ImGui.Text(item);
            });
        }

        if (ImGui.Button("Discard Junk Items"))
        {
            junkpile.DiscardItems();
        }

        if (ImGui.Button("Clear Junkpile"))
        {
            junkpile.ClearJunkpile();
        }
    }
}
