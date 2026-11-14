
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppComponent } from './app.component';
import { routes } from './app.routes';

describe('AppComponent', () => {

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter(routes)]
    }).compileComponents();
  });

  it('should create the application', () => {
    const fixture =
      TestBed.createComponent(AppComponent);

    expect(fixture.componentInstance)
      .toBeTruthy();
  });

  it('should have the correct title', () => {
    const fixture =
      TestBed.createComponent(AppComponent);

    expect(fixture.componentInstance.title)
      .toBe('MentorBooking');
  });

  it('should contain the router outlet', () => {
    const fixture =
      TestBed.createComponent(AppComponent);

    fixture.detectChanges();

    const element =
      fixture.nativeElement as HTMLElement;

    expect(
      element.querySelector('router-outlet')
    ).not.toBeNull();
  });

});
