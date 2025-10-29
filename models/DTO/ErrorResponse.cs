namespace YandexSpeech.models.DTO
{
    public class ErrorResponse
    {
        public string Message { get; set; } = string.Empty;

        public static ErrorResponse FromMessage(string message)
        {
            return new ErrorResponse { Message = message ?? string.Empty };
        }
    }
}
