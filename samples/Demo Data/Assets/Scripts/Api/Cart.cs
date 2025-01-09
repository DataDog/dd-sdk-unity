// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Demo.Unity.Api
{
    public class CartBreakdown
    {
        public float OrderValue { get; private set; }
        public float Tax { get; private set; }
        public float Shipping { get; private set; }

        public float Total => OrderValue + Tax + Shipping;

        public CartBreakdown(float orderValue, float tax, float shipping)
        {
            OrderValue = orderValue;
            Tax = tax;
            Shipping = shipping;
        }

        public static CartBreakdown Empty()
        {
            return new CartBreakdown(0.0f, 0.0f, 0.0f);
        }
    }

    public class Cart
    {
        const float TaxPercent = 0.18f;
        const float ShippingPerItem = 10.0f;

        private Dictionary<Product, int> CartItems = new Dictionary<Product, int>();

        public bool IsEmpty => CartItems.Count == 0;

        public void Clear()
        {
            CartItems.Clear();
        }

        public void AddItem(Product product, int quantity)
        {
            if (CartItems.ContainsKey(product))
            {
                CartItems[product] += quantity;
            }
            else
            {
                CartItems.Add(product, quantity);
            }
        }

        public void RemoveItem(Product product, int quantity)
        {
            if (CartItems.ContainsKey(product))
            {
                CartItems[product] -= quantity;
                if (CartItems[product] <= 0)
                {
                    CartItems.Remove(product);
                }
            }
        }

        public bool HasProduct(Product product)
        {
            return CartItems.ContainsKey(product);
        }

        public CartBreakdown GenerateBreakdown()
        {
            if (CartItems.Count == 0)
            {
                return CartBreakdown.Empty();
            }

            float orderValue = 0.0f;
            foreach (var item in CartItems)
            {
                orderValue += item.Key.priceFloat * item.Value;
            }

            float tax = orderValue * TaxPercent;
            float shipping = CartItems.Count * ShippingPerItem;

            return new CartBreakdown(orderValue, tax, shipping);
        }
    }
}
