import { EnumDialogButtons } from "./dialogbuttons.enum";

export class DialogOptions {
    title!: string;
    content!: string;
    buttons: EnumDialogButtons = EnumDialogButtons.OK;
    allowClose: boolean = true;
    width: number | undefined;
    height: number | undefined;
}