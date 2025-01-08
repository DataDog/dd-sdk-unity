// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;

[System.Serializable]
public class Category
{
    public string id { get; set; }
    public string title { get; set; }
    public string cover { get; set; }
}

[System.Serializable]
public class Product
{
    public int id { get; set; }
    public string name { get; set; }
    public string price { get; set; }

    public float priceFloat => float.Parse(price);

    public string cover { get; set; }
    public bool isInStock { get; set; }
}

[System.Serializable]
public class Checkout
{
    public string cardNumber { get; set; }
    public int cvc { get; set; }
    public string exp { get; set; }

    public static Checkout Random()
    {
        return new Checkout
        {
            cardNumber = $"{RandomNumber(16)}",
            cvc = RandomNumber(3),
            exp = $"{RandomNumber(4)}"
        };
    }

    private static int RandomNumber(int length)
    {
        int result = 0;
        for (int i = 0; i < length; ++i) {
            // First digit must be greater than 1 (last generated)
            int digit;
            if (i == length - 1) {
                digit = UnityEngine.Random.RandomRange(0, 9) + 1;
            } else {
                digit = UnityEngine.Random.RandomRange(0, 10);
            }
            result += digit * (int)Math.Pow(10, i);
        }
        return result;
    }
}

[System.Serializable]
public class Payment
{
    public Checkout checkout { get; set; }
}

[System.Serializable]
public class PaymentResponse
{
    public string cardNumber;
    public int cvc;
    public string exp;
    public string email;
    public string createdAt;
    public string updatedAt;
}
