import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RecognitionTasksComponent } from './recognition-tasks.component';

describe('RecognitionTasksComponent', () => {
  let component: RecognitionTasksComponent;
  let fixture: ComponentFixture<RecognitionTasksComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RecognitionTasksComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(RecognitionTasksComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
