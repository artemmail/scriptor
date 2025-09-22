using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

[Table("YoutubeChannels")]
public class YoutubeChannel
{
    /// <summary>
    /// Идентификатор канала (YouTube ChannelId)
    /// </summary>
    [Key]
    [Required]
    [Column("ChannelId")]
    public string ChannelId { get; set; } = null!;

    /// <summary>
    /// Название канала
    /// </summary>
    [Required]
    public string ChannelTitle { get; set; } = null!;
}