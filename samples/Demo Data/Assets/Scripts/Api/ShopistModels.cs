// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

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
    public string cover { get; set; }
    public bool isInStock { get; set; }
}
