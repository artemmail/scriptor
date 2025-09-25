using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB;

/// <summary>
/// Статусы распознания, разбитые на группы по трёхзначным значениям
/// для более наглядной логики.
/// </summary>
public enum RecognizeStatus
{
    // 100–199: первичные состояния
    Created = 100,

    // 200–299: основные этапы (примерно связанные с преобразованием)
    Converting = 200,
    Uploading = 210,
    Recognizing = 220,
    RetrievingResult = 230,
    ApplyingPunctuation = 240,

    InProgress = 250,

    // 300–399: этапы, связанные с обработкой YouTube-субтитров
    FetchingMetadata = 300,      // Получение списка треков (метаданных)
    DownloadingCaptions = 310,    // Скачивание субтитров
    SegmentingCaptions = 320,     // Сегментация субтитров
    ApplyingPunctuationSegment = 330, // Пунктуация по одному сегменту

    // 900+: финальные станы
    Done = 900,

    // 999 или 400–499 — в зависимости от ваших предпочтений под «ошибки»
    Error = 999
}

public enum RecognitionBillingType
{
    Unknown = 0,
    Subscription = 1,
    Wallet = 2,
    FreeTier = 3,
    Bonus = 4
}

public class RecognizeResultDB
{
    public bool done { get; set; }
    public string response { get; set; } = string.Empty;
    public string id { get; set; } = string.Empty;
    public DateTime createdAt { get; set; }
    public string createdBy { get; set; } = string.Empty;
    public DateTime modifiedAt { get; set; }

    // Заменяем string на RecognizeStatus
    public RecognizeStatus Status { get; set; }
}

[Table("SpeechRecognitionTasks")]
public class SpeechRecognitionTask
{
    [Key]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Путь к исходному mp3 (или другому) файлу на локальной машине.
    /// </summary>
    public string? OriginalFilePath { get; set; }

    /// <summary>
    /// Путь к сконвертированному файлу .opus на локальной машине.
    /// </summary>
    public string? OpusFilePath { get; set; }

    /// <summary>
    /// Название S3-бакета, куда загружаем файл (например, "ruticker").
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// Ключ (путь) объекта в S3 (например, myaudio.opus).
    /// </summary>
    public string? ObjectKey { get; set; }

    /// <summary>
    /// OperationId, полученный при запуске распознавания в Yandex STT.
    /// </summary>
    public string? OperationId { get; set; }

    /// <summary>
    /// Финальный результат распознанного текста (либо можно хранить JSON).
    /// </summary>
    public string? RecognizedText { get; set; }

    /// <summary>
    /// Текущий статус задачи.
    /// </summary>
    public RecognizeStatus? Status { get; set; }

    /// <summary>
    /// Флаг завершения задачи (дополнительно к Status == Done).
    /// </summary>
    public bool Done { get; set; } = false;

    /// <summary>
    /// Дата создания.
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Дата последнего изменения.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// Кто инициировал задачу (логин / имя пользователя и т.д.).
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Кто инициировал задачу (логин / имя пользователя и т.д.).
    /// </summary>
    public string? Result { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Флаг, что задача относится к субтитрам YouTube
    /// </summary>
    public bool IsSubtitleTask { get; set; }

    /// <summary>
    /// ID видео в YouTube, если IsSubtitleTask = true
    /// </summary>
    public string? YoutubeId { get; set; }

    /// <summary>
    /// Язык для субтитров (возможно, null)
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Тип тарификации, применённый к задаче.
    /// </summary>
    public RecognitionBillingType BillingType { get; set; } = RecognitionBillingType.Unknown;

    /// <summary>
    /// Сумма, списанная за задачу.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? BillingAmount { get; set; }

    /// <summary>
    /// Валюта списания.
    /// </summary>
    [MaxLength(8)]
    public string? BillingCurrency { get; set; }

    /// <summary>
    /// Дата списания.
    /// </summary>
    public DateTime? BilledAt { get; set; }

    /// <summary>
    /// Идентификатор транзакции кошелька, если она была создана.
    /// </summary>
    public Guid? WalletTransactionId { get; set; }

    /// <summary>
    /// Идентификатор подписки, использованной для тарификации.
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// Ссылка на запись об использовании лимита.
    /// </summary>
    public Guid? UsageRecordId { get; set; }

    /// <summary>
    /// Дополнительный комментарий для биллинга.
    /// </summary>
    [MaxLength(512)]
    public string? BillingComment { get; set; }
}
