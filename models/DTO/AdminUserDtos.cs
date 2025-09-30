using System;
using System.Collections.Generic;

namespace YandexSpeech.models.DTO
{
    public class AdminUserListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int RecognizedVideos { get; set; }
        public DateTime RegisteredAt { get; set; }
        public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> YoutubeCaptionIps { get; set; } = Array.Empty<string>();
    }

    public class AdminUsersPageDto
    {
        public IReadOnlyCollection<AdminUserListItemDto> Items { get; set; } = Array.Empty<AdminUserListItemDto>();
        public int TotalCount { get; set; }
    }

    public class UpdateUserRolesRequest
    {
        public List<string> Roles { get; set; } = new List<string>();
    }
}
