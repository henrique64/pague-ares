import { Injectable } from "@angular/core";
import { MatDialog, MatDialogConfig } from "@angular/material/dialog";
import { Observable } from "rxjs";
import { DialogComponent } from "./dialog.component";
import { EnumDialogButtons } from "./dialogbuttons.enum";
import { DialogOptions } from "./dialogoptions.model";
import { EnumDialogResult } from "./dialogresult.enum";

@Injectable()
export class DialogService {

  constructor(private dialog: MatDialog) {

  }

  open(text: string, options: DialogOptions): Observable<EnumDialogResult> {
    let opt = { content: text } as DialogOptions;

    if (options)
      opt = { ...opt, ...options }

    let config = {
      data: opt,
      disableClose: !options.allowClose,
      width: options.width?.toString() || undefined,
      height: options.height?.toString() || undefined,
      panelClass: 'app-dialog-pane'
    } as MatDialogConfig

    const dialogRef = this.dialog.open(DialogComponent, config);

    return dialogRef.afterClosed();
  }

  confirm(text: string, title: string = ""): Observable<EnumDialogResult> {
    const options = {
      buttons: EnumDialogButtons.Yes | EnumDialogButtons.No,
      title: title
    } as DialogOptions;

    return this.open(text, options)
  }

  alert(text: string, title: string = ""): Observable<EnumDialogResult> {
    const options = {
      buttons: EnumDialogButtons.OK,
      title: title
    } as DialogOptions;

    return this.open(text, options)
  }

}
