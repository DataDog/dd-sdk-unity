// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

namespace Datadog.Unity.Rum
{
    public static class EnumHelpers
    {
        public static RumHttpMethod HttpMethodFromString(string method)
        {
            switch (method.ToLower())
            {
                case "post": return RumHttpMethod.Post;
                case "get": return RumHttpMethod.Get;
                case "head": return RumHttpMethod.Head;
                case "put": return RumHttpMethod.Put;
                case "delete": return RumHttpMethod.Delete;
                case "patch": return RumHttpMethod.Patch;
                default: return RumHttpMethod.Get;
            }
        }

        public static RumResourceType ResourceTypeFromContentType(string contentType)
        {
            if (contentType == null)
            {
                return RumResourceType.Native;
            }

            var contentTypeParts = contentType.Split('/');
            switch (contentTypeParts[0].ToLower())
            {
                case "image":
                    return RumResourceType.Image;
                case "audio":
                case "video":
                    return RumResourceType.Media;
                case "font":
                    return RumResourceType.Font;
            }

            if (contentTypeParts.Length > 1)
            {
                switch (contentTypeParts[1].ToLower())
                {
                    case "javascript":
                        return RumResourceType.Js;
                    case "css":
                        return RumResourceType.Css;
                }
            }

            return RumResourceType.Native;
        }
    }
}
