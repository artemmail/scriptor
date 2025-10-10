export interface RecognitionProfile {
  id: number;
  request: string;
  clarificationTemplate: string | null;
  openAiModel: string;
  segmentBlockSize: number;
}

export interface RecognitionProfileInput {
  request: string;
  clarificationTemplate?: string | null;
  openAiModel: string;
  segmentBlockSize: number;
}
