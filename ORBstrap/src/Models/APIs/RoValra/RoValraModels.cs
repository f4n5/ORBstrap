using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Crypt3d.Models.APIs.RoValra
{
    public class RoValraGeolocation
    {
        [JsonPropertyName("location")]
        public RoValraLocation? Location { get; set; }
    }

    public class RoValraLocation
    {
        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    public class RoValraProcessServerBody
    {
        [JsonPropertyName("place_id")]
        public long PlaceId { get; set; }

        [JsonPropertyName("server_ids")]
        public List<string> ServerIds { get; set; } = new();
    }

    public class RoValraDatacenter
    {
        [JsonPropertyName("location")]
        public RoValraLocation? Location { get; set; }

        // Additional fields as returned by the API
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    public class RoValraServersResponse
    {
        [JsonPropertyName("servers")]
        public List<RoValraServerEntry>? Servers { get; set; }
    }

    public class RoValraServerEntry
    {
        [JsonPropertyName("server_id")]
        public string? ServerId { get; set; }

        [JsonPropertyName("first_seen")]
        public DateTime? FirstSeen { get; set; }
    }

    public class RoValraServerDetails
    {
        [JsonPropertyName("server_id")]
        public string? ServerId { get; set; }

        [JsonPropertyName("first_seen")]
        public DateTime? FirstSeen { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("is_full")]
        public bool IsFull { get; set; }

        [JsonPropertyName("place_version")]
        public int? PlaceVersion { get; set; }
    }
}
