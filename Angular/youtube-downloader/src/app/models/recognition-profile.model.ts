export interface RecognitionProfile {
  id: number;
  name: string;
  displayedName: string;
  request: string;
  clarificationTemplate: string | null;
  openAiModel: string;
  segmentBlockSize: number;
}

export interface RecognitionProfileInput {
  name: string;
  displayedName: string;
  request: string;
  clarificationTemplate?: string | null;
  openAiModel: string;
  segmentBlockSize: number;
}
