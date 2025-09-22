import { Component, AfterViewInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule, MatTableDataSource } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCardModule } from '@angular/material/card';
import { MatPaginator, MatPaginatorModule } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AudioFileService, AudioFile } from '../services/audio-file.service';
import { SpeechWorkflowService } from '../services/SpeechWorkflow.service';

@Component({
  selector: 'app-audio-files',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCardModule,
    MatPaginatorModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './audio-files.component.html',
  styleUrls: ['./audio-files.component.css']
})
export class AudioFilesComponent implements AfterViewInit {
  displayedColumns = ['originalFileName', 'uploadedAt', 'actions', 'recognize'];
  dataSource = new MatTableDataSource<AudioFile>([]);
  selectedFile?: File;
  uploading = false;
  errorMessage = '';
  recognizing: Record<string, boolean> = {};

  @ViewChild(MatPaginator) paginator!: MatPaginator;

  constructor(private fileService: AudioFileService, private speechService: SpeechWorkflowService) {
    this.loadFiles();
  }

  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator;
  }

  loadFiles(): void {
    this.fileService.list().subscribe({
      next: files => this.dataSource.data = files,
      error: err => this.errorMessage = err.message
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0];
  }

  upload(): void {
    if (!this.selectedFile) return;
    this.uploading = true;
    this.fileService.upload(this.selectedFile).subscribe({
      next: () => {
        this.uploading = false;
        this.selectedFile = undefined;
        this.loadFiles();
      },
      error: err => {
        this.uploading = false;
        this.errorMessage = err.message;
      }
    });
  }

  delete(id: string): void {
    this.fileService.delete(id).subscribe({
      next: () => this.loadFiles(),
      error: err => this.errorMessage = err.message
    });
  }

  onRecognize(fileId: string): void {
    this.recognizing[fileId] = true;
    this.speechService.startRecognition(fileId).subscribe({
      next: taskId => {
        console.log('Recognition task started:', taskId);
        this.recognizing[fileId] = false;
      },
      error: err => {
        console.error(err);
        this.recognizing[fileId] = false;
      }
    });
  }
}
