using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/admin/recognition-profiles")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public class RecognitionProfilesController : ControllerBase
    {
        private readonly MyDbContext _dbContext;

        public RecognitionProfilesController(MyDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<RecognitionProfileDto>>> GetAll(CancellationToken cancellationToken)
        {
            var profiles = await _dbContext.RecognitionProfiles
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(p => new RecognitionProfileDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    DisplayedName = p.DisplayedName,
                    Request = p.Request,
                    ClarificationTemplate = p.ClarificationTemplate,
                    OpenAiModel = p.OpenAiModel,
                    SegmentBlockSize = p.SegmentBlockSize
                })
                .ToListAsync(cancellationToken);

            return Ok(profiles);
        }

        [HttpPost]
        public async Task<ActionResult<RecognitionProfileDto>> Create(
            [FromBody] UpsertRecognitionProfileRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest("Request body is required");
            }

            var validationResult = ValidateRequest(request);
            if (validationResult is { IsValid: false, ErrorMessage: { } errorMessage })
            {
                return BadRequest(new { message = errorMessage });
            }

            var profile = new RecognitionProfile
            {
                Name = validationResult.Name!,
                DisplayedName = validationResult.DisplayedName!,
                Request = validationResult.Request!,
                ClarificationTemplate = validationResult.ClarificationTemplate,
                OpenAiModel = validationResult.OpenAiModel!,
                SegmentBlockSize = validationResult.SegmentBlockSize!.Value
            };

            _dbContext.RecognitionProfiles.Add(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Created($"/api/admin/recognition-profiles/{profile.Id}", Map(profile));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<RecognitionProfileDto>> Update(
            int id,
            [FromBody] UpsertRecognitionProfileRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest("Request body is required");
            }

            var profile = await _dbContext.RecognitionProfiles
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            if (profile == null)
            {
                return NotFound();
            }

            var validationResult = ValidateRequest(request);
            if (validationResult is { IsValid: false, ErrorMessage: { } errorMessage })
            {
                return BadRequest(new { message = errorMessage });
            }

            profile.Request = validationResult.Request!;
            profile.Name = validationResult.Name!;
            profile.DisplayedName = validationResult.DisplayedName!;
            profile.ClarificationTemplate = validationResult.ClarificationTemplate;
            profile.OpenAiModel = validationResult.OpenAiModel!;
            profile.SegmentBlockSize = validationResult.SegmentBlockSize!.Value;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(Map(profile));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            var profile = await _dbContext.RecognitionProfiles
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            if (profile == null)
            {
                return NotFound();
            }

            _dbContext.RecognitionProfiles.Remove(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        private static RecognitionProfileDto Map(RecognitionProfile profile) => new RecognitionProfileDto
        {
            Id = profile.Id,
            Name = profile.Name,
            DisplayedName = profile.DisplayedName,
            Request = profile.Request,
            ClarificationTemplate = profile.ClarificationTemplate,
            OpenAiModel = profile.OpenAiModel,
            SegmentBlockSize = profile.SegmentBlockSize
        };

        private static ValidationResultModel ValidateRequest(UpsertRecognitionProfileRequest request)
        {
            var result = new ValidationResultModel();

            var trimmedName = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                result.IsValid = false;
                result.ErrorMessage = "Name is required";
                return result;
            }

            if (trimmedName!.Length > 200)
            {
                result.IsValid = false;
                result.ErrorMessage = "Name cannot exceed 200 characters";
                return result;
            }

            var trimmedDisplayedName = request.DisplayedName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDisplayedName))
            {
                result.IsValid = false;
                result.ErrorMessage = "DisplayedName is required";
                return result;
            }

            if (trimmedDisplayedName!.Length > 200)
            {
                result.IsValid = false;
                result.ErrorMessage = "DisplayedName cannot exceed 200 characters";
                return result;
            }

            var trimmedRequest = request.Request?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedRequest))
            {
                result.IsValid = false;
                result.ErrorMessage = "Request is required";
                return result;
            }

            var trimmedModel = request.OpenAiModel?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedModel))
            {
                result.IsValid = false;
                result.ErrorMessage = "OpenAiModel is required";
                return result;
            }

            if (request.SegmentBlockSize is null || request.SegmentBlockSize <= 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "SegmentBlockSize must be greater than zero";
                return result;
            }

            result.IsValid = true;
            result.Request = trimmedRequest;
            result.OpenAiModel = trimmedModel;
            result.Name = trimmedName;
            result.DisplayedName = trimmedDisplayedName;
            result.ClarificationTemplate = string.IsNullOrWhiteSpace(request.ClarificationTemplate)
                ? null
                : request.ClarificationTemplate.Trim();
            result.SegmentBlockSize = request.SegmentBlockSize;

            return result;
        }

        private sealed class ValidationResultModel
        {
            public bool IsValid { get; set; }
            public string? ErrorMessage { get; set; }
            public string? Name { get; set; }
            public string? DisplayedName { get; set; }
            public string? Request { get; set; }
            public string? ClarificationTemplate { get; set; }
            public string? OpenAiModel { get; set; }
            public int? SegmentBlockSize { get; set; }
        }
    }
}
