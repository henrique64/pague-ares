import { Component, OnInit, OnDestroy, Inject } from '@angular/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';

import { EnumDialogButtons } from './dialogbuttons.enum';
import { DialogOptions } from './dialogoptions.model';
import { EnumDialogResult } from './dialogresult.enum';

@Component({
  selector: 'app-dialog',
  templateUrl: './dialog.component.html',
  styleUrls: ["./dialog.component.scss"]
})
export class DialogComponent implements OnInit, OnDestroy {

  options!: DialogOptions;

  constructor(
    private modal: MatDialogRef<DialogComponent>,
    @Inject(MAT_DIALOG_DATA) data: DialogOptions
  ) {
    this.options = data;
  }

  ngOnInit(): void {
    this.modal.beforeClosed().subscribe((x) => {
      if (!x) {
        this.modal.close(EnumDialogResult.None)
      }
    });
  }

  ngOnDestroy(): void {

  }

  close(): void {
    this.modal.close();
  }

  ok() {
    this.modal.close(EnumDialogResult.OK);
  }

  cancel() {
    this.modal.close(EnumDialogResult.Cancel);
  }

  yes() {
    this.modal.close(EnumDialogResult.Yes);
  }

  no() {
    this.modal.close(EnumDialogResult.No);
  }

  get hasOk(): boolean {
    return EnumDialogButtons.OK === (this.options.buttons & EnumDialogButtons.OK);
  }

  get hasYes(): boolean {
    return EnumDialogButtons.Yes === (this.options.buttons & EnumDialogButtons.Yes);
  }

  get hasNo(): boolean {
    return EnumDialogButtons.No === (this.options.buttons & EnumDialogButtons.No);
  }

  get hasCancel(): boolean {
    return EnumDialogButtons.Cancel === (this.options.buttons & EnumDialogButtons.Cancel);
  }

}
