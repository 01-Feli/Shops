﻿using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Eventing;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using Shops.Database.Models;
using Shops.Events;
using System;
using System.Collections.Generic;

namespace Shops.Shops
{
    public class ShopSellItem : IShop
    {
        private readonly ShopsPlugin m_ShopsPlugin;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly IEconomyProvider m_EconomyProvider;
        private readonly IEventBus m_EventBus;

        public ShopSellItem(SellItem shop,
            ShopsPlugin shopsPlugin,
            IStringLocalizer stringLocalizer,
            IEconomyProvider economyProvider,
            IEventBus eventBus)
        {
            Id = (ushort)shop.Id;
            Price = shop.SellPrice;

            m_ShopsPlugin = shopsPlugin;
            m_StringLocalizer = stringLocalizer;
            m_EconomyProvider = economyProvider;
            m_EventBus = eventBus;
        }

        public ushort Id;

        public decimal Price;

        public async UniTask Interact(UnturnedUser user, int amount)
        {
            await UniTask.SwitchToMainThread();

            decimal totalPrice = Price * amount;

            ItemAsset asset = (ItemAsset)Assets.find(EAssetType.ITEM, Id);

            if (asset == null)
            {
                throw new Exception($"Item asset for Id '{Id}' not found");
            }

            var sellingEvent = new PlayerSellingItemEvent(user, Id, amount, Price);
            await m_EventBus.EmitAsync(m_ShopsPlugin, this, sellingEvent);

            if (sellingEvent.IsCancelled) return;

            await UniTask.SwitchToMainThread();

            List<InventorySearch> foundItems = user.Player.Player.inventory.search(Id, true, true);

            if (foundItems.Count < amount)
            {
                throw new UserFriendlyException(m_StringLocalizer["item_sell_not_enough", new
                {
                    ItemName = asset.itemName,
                    ItemId = asset.id,
                    Amount = amount
                }]);
            }

            for (int i = 0; i < amount; i++)
            {
                InventorySearch found = foundItems[i];

                byte index = user.Player.Player.inventory.getIndex(found.page, found.jar.x, found.jar.y);

                user.Player.Player.inventory.removeItem(found.page, index);
            }

            await UniTask.SwitchToThreadPool();

            decimal newBalance = await m_EconomyProvider.UpdateBalanceAsync(user.Id, user.Type, totalPrice, $"Sold {amount} {asset.itemName}s");

            await user.PrintMessageAsync(m_StringLocalizer["shops:success:item_sell",
                new
                {
                    ItemName = asset.itemName,
                    ItemId = asset.id,
                    Amount = amount,
                    SellPrice = totalPrice,
                    Balance = newBalance,
                    m_EconomyProvider.CurrencyName,
                    m_EconomyProvider.CurrencySymbol,
                }]);

            var soldEvent = new PlayerSoldItemEvent(user, Id, amount, Price);
            await m_EventBus.EmitAsync(m_ShopsPlugin, this, soldEvent);
        }
    }
}