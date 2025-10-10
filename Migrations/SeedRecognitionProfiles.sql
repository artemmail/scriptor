-- Seed default recognition profiles derived from
-- IntegratedFormattingPunctuationService and PunctuationService.

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM RecognitionProfiles WHERE Name = 'integrated-formatting')
BEGIN
    INSERT INTO RecognitionProfiles (Name, DisplayedName, Request, ClarificationTemplate, OpenAiModel, SegmentBlockSize)
    VALUES (
        'integrated-formatting',
        N'Форматирование и пунктуация',
        N'You transform raw speech recognition output into the final Markdown dialogue.
Restore punctuation, split long monologues into natural sentences and attribute every replica to a speaker.
Use bold speaker labels followed by a colon (e.g. **Speaker 1:**) and preserve the narrative style from the
previously formatted segment that can be provided as assistant context. Do not add explanations or commentary.',
        NULL,
        'gpt-4.1-mini',
        600
    );
END;

IF NOT EXISTS (SELECT 1 FROM RecognitionProfiles WHERE Name = 'punctuation-only')
BEGIN
    INSERT INTO RecognitionProfiles (Name, DisplayedName, Request, ClarificationTemplate, OpenAiModel, SegmentBlockSize)
    VALUES (
        'punctuation-only',
        N'Пунктуация и Markdown',
        N'You are a service that adds punctuation and Markdown formatting to recognized audio text.
Ensure that if a previous formatted segment is provided, the style remains consistent in the new segment.
Do not include any additional comments or explanations.',
        NULL,
        'gpt-4.1-mini',
        600
    );
END;
