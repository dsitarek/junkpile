using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Junkpile
{
    public class Junkpile
    {
        public unsafe Junkpile(IChatGui ichat)
        {
            Junk = new List<InventoryItem>();
            JunkItemNames = new List<string>();
            agentInventoryContext = AgentInventoryContext.Instance();
            inventoryManager = InventoryManager.Instance();
            chat = ichat;
            isDiscarding = false;
            signal = new SemaphoreSlim(0);

        }
        public List<InventoryItem> Junk { get; set; }
        public List<string> JunkItemNames { get; set; }
        private unsafe InventoryManager* inventoryManager;
        private unsafe AgentInventoryContext* agentInventoryContext;
        private static readonly int[] InventoryContainerArray = [0, 1, 2, 3];
        private IChatGui chat;
        public bool isDiscarding { get; set; }
        public SemaphoreSlim signal;

        public unsafe void AddRemoveJunkItem(ulong itemId, ushort container, uint slot)
        {
            bool hq = itemId > 1000000;
            if (hq) itemId = itemId - 1000000;
            if (!InventoryContainerArray.Contains(container)) return;
            var manager = InventoryManager.Instance();
            var inventory = *manager->GetInventoryContainer((InventoryType)container);
            SeString? itemStringInfo = "";

            for (var i = 0; i < inventory.Size; i++)
            {
                if (inventory.Items[i].ItemID == itemId && inventory.Items[i].Slot == slot)
                {
                    var itemInfo = *inventory.GetInventorySlot(inventory.Items[i].Slot);
                    SeStringBuilder sb = new SeStringBuilder();
                    sb.AddItemLink((uint)itemId, hq);
                    if (!Junk.Any(x => x.ItemID == itemInfo.ItemID && x.Slot == itemInfo.Slot))
                    {
                        Junk.Add(itemInfo);
                        JunkItemNames.Add(sb.ToString());
                        itemStringInfo = sb.Append(" added to junkpile.").BuiltString;

                        var response = new XivChatEntry()
                        {
                            Message = itemStringInfo,
                            Type = XivChatType.Debug

                        };
                        chat.Print(response);
                    }
                    else
                    {
                        Junk.Remove(itemInfo);
                        JunkItemNames.Remove(sb.ToString());
                    }
                }
                continue;
            }
        }

        public async void DiscardItems()
        {
            isDiscarding = true;
            var items = Junk;
            foreach (var item in items)
            {
                Discard(item);
                await signal.WaitAsync();
            }
            Junk.Clear();
            JunkItemNames.Clear();
            isDiscarding = false;
        }

        private unsafe void Discard(InventoryItem item)
        {
            var inventoryType = (InventoryType)item.LinkedInventoryType;
            var inventory = *inventoryManager->GetInventoryContainer(item.Container);
            var itemInfo = inventory.GetInventorySlot(item.Slot);
            agentInventoryContext->DiscardItem(itemInfo, itemInfo->Container, itemInfo->Slot, agentInventoryContext->OwnerAddonId);
        }

        public void DidDiscard(AddonEvent type, AddonArgs args)
        {
            signal.Release();
        }
    }
}
