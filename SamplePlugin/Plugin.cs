using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Junkpile.Windows;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game.Gui.ContextMenu;
using System;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using ClickLib.Clicks;
using System.Threading;
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
    private List<InventoryItem> Junkpile { get; init; }
    private List<string> JunkItemNames { get; init; }
    private IAddonLifecycle AddonLifecycle {  get; init; }
    private bool isDiscarding {  get; set; }
    private SemaphoreSlim signal;

    private static readonly int[] InventoryContainerArray = [0, 1, 2, 3];

    private unsafe AgentInventoryContext* agentInventoryContext;
    private unsafe InventoryManager* inventoryManager;

    private unsafe void SetContexts()
    {
        agentInventoryContext = AgentInventoryContext.Instance();
        inventoryManager = InventoryManager.Instance();
    }

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
        if(isDiscarding) ClickSelectYesNo.Using(addon).Yes();
    }

    public void UpdateDiscardStatus(AddonEvent type, AddonArgs args)
    {
        signal.Release();
        Chat.Print("release");
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
        Junkpile = new List<InventoryItem>();
        JunkItemNames = new List<string>();
        ContextMenu = contextMenu;
        AddonLifecycle = addonLc;
        isDiscarding = false;
        SetContexts();
        signal = new SemaphoreSlim(0);

        contextMenu.AddMenuItem(ContextMenuType.Inventory, new MenuItem()
        {
            Name = "Add to junk",
            Priority = -2,
            OnClicked = (MenuItemClickedArgs a) =>
            {
                if (a.Target is MenuTargetInventory item && item.TargetItem != null)
                {
                    var itemId = item.TargetItem.Value.ItemId;
                    var container = (ushort)item.TargetItem.Value.ContainerType;
                    AddItemToJunk(itemId, container);
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
        AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "SelectYesno", UpdateDiscardStatus);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, JunkItemNames);

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
        signal.Dispose();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleMainUI();

    }

    private unsafe void AddItemToJunk(ulong itemId, ushort container)
    {
        bool hq = itemId > 1000000;
        if (hq) itemId = itemId - 1000000;
        if (!InventoryContainerArray.Contains(container)) return;
        var manager = InventoryManager.Instance();
        var inventory = *manager->GetInventoryContainer((InventoryType)container);
        SeString? itemStringInfo = "";

        for (var i = 0; i < inventory.Size; i++)
        {
            if (inventory.Items[i].ItemID == itemId)
            {
                var itemInfo = *inventory.GetInventorySlot(inventory.Items[i].Slot);
                if(!Junkpile.Any(x => x.ItemID == itemInfo.ItemID))
                {
                    Junkpile.Add(itemInfo);
                    SeStringBuilder sb = new SeStringBuilder();
                    sb.AddItemLink((uint)itemId, hq);
                    JunkItemNames.Add(sb.ToString());
                    itemStringInfo = sb.Append(" added to junkpile.").BuiltString;

                    var response = new XivChatEntry()
                    {
                        Message = itemStringInfo,
                        Type = XivChatType.Debug

                    };
                    Chat.Print(response);
                }
            }
            continue;
        }
    }
    public async void DiscardItems()
    {
        isDiscarding = true;
        var items = Junkpile;
        //var invContext = AgentInventoryContext.Instance();
        //var manager = InventoryManager.Instance();
        foreach (var item in items)
        {
            Discard(item);
            await signal.WaitAsync();
        }
        isDiscarding = false;
        Junkpile.Clear();
        JunkItemNames.Clear();
    }

    private unsafe void Discard(InventoryItem item)
    {
        var inventoryType = (InventoryType)item.LinkedInventoryType;
        var inventory = *inventoryManager->GetInventoryContainer(item.Container);
        var itemInfo = inventory.GetInventorySlot(item.Slot);
        agentInventoryContext->DiscardItem(itemInfo, itemInfo->Container, itemInfo->Slot, agentInventoryContext->OwnerAddonId);
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
