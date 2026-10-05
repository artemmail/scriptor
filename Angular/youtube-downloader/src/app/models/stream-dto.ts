// src/app/models/stream-dto.ts

export interface QualityLabel {
  label: string;
  maxHeight: number;
  framerate: number;
  isHighDefinition: boolean;
}

export interface StreamDto {
  type: 'video' | 'audio' | 'muxed';
  qualityLabel: QualityLabel | string | null;
  container: string;
  language: string | null;
  languageCode?: string | null;
  codec: string;
  bitrate: number;
  size: number;
}
