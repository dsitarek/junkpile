using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Junkpile.Windows;
using Dalamud.Game.Gui.ContextMenu;
using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using ClickLib.Clicks;

namespace Junkpile;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/junk";

    private DalamudPluginInterface PluginInterface { get; init; }
    private ICommandManager CommandManager { get; init; }
    private IChatGui Chat { get; init; }
    private IContextMenu ContextMenu { get; init; }
    private IGameGui GameUI { get; init; }
    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Junkpile");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private Junkpile Junkpile { get; init; }
    private IAddonLifecycle AddonLifecycle {  get; init; }

    private unsafe void SelectYesNoSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        SelectYes((nint)addon);
    }

    private unsafe void SelectYes(IntPtr addon)
    {
        var dataPtr = (AddonSelectYesNoOnSetupData*)addon;
        var addonPtr = (AddonSelectYesno*)addon;
        var yesButton = addonPtr->YesButton;
        if (yesButton != null && !yesButton->IsEnabled)
        {
            var flagsPtr = (ushort*)&yesButton->AtkComponentBase.OwnerNode->AtkResNode.NodeFlags;
            *flagsPtr ^= 1 << 5;
        }
        if(Junkpile.isDiscarding) ClickSelectYesNo.Using(addon).Yes();
    }

    public Plugin(
        [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
        [RequiredVersion("1.0")] ICommandManager commandManager,
        [RequiredVersion("1.0")] ITextureProvider textureProvider, IChatGui chat, IGameGui gui, IContextMenu contextMenu, IAddonLifecycle addonLc)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        Chat = chat;
        GameUI = gui;
        ContextMenu = contextMenu;
        AddonLifecycle = addonLc;
        Junkpile = new Junkpile(chat);

        contextMenu.AddMenuItem(ContextMenuType.Inventory, new MenuItem()
        {
            Name = "Add/remove to/from junk",
            Priority = -2,
            OnClicked = (MenuItemClickedArgs a) =>
            {
                if (a.Target is MenuTargetInventory item && item.TargetItem != null)
                {
                    var itemId = item.TargetItem.Value.ItemId;
                    var slot = item.TargetItem.Value.InventorySlot;
                    var container = (ushort)item.TargetItem.Value.ContainerType;
                    Junkpile.AddRemoveJunkItem(itemId, container, slot);
                } else
                {
                    chat.Print("Only Inventory items can be junked.");
                }
                return;
            }
        }); ;

        contextMenu.AddMenuItem(ContextMenuType.Inventory, new MenuItem()
        {
            Name = "View Junkpile",
            Priority= -1,
            IsSubmenu = true,
            OnClicked = (MenuItemClickedArgs a) =>
            {
                ToggleMainUI();
                return;
            }
        });


        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesNoSetup);
        AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "SelectYesno", Junkpile.DidDiscard);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, Junkpile);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens junk window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Junkpile.signal.Dispose();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleMainUI();

    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();

    [StructLayout(LayoutKind.Explicit, Size = 0x10)]
    private struct AddonSelectYesNoOnSetupData
    {
        [FieldOffset(0x8)]
        public IntPtr TextPtr;
    }
}
