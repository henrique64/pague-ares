import { Component, OnInit } from '@angular/core';

@Component({
    selector: 'app-loading',
    template: `
    <div class="loading"></div>
  `,
    styleUrls: ['./loading.component.scss'],
    standalone: false
})
export class LoadingComponent implements OnInit {
  constructor() { }

  ngOnInit(): void { }
}
