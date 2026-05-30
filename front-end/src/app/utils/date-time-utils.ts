export class DateTimeUtils {
  private static pad(number, length) {
    var str = "" + number;
    while (str.length < length) {
      str = "0" + str;
    }

    return str;
  }

  private static pad2(string: number): string {
    return this.pad(string, 2);
  }

  static toSqlDateTimeFormat(date: Date): string {
    var dt = date;
    var dtstring =
      dt.getFullYear() +
      "-" +
      this.pad2(dt.getMonth() + 1) +
      "-" +
      this.pad2(dt.getDate()) +
      " " +
      this.pad2(dt.getHours()) +
      ":" +
      this.pad2(dt.getMinutes()) +
      ":" +
      this.pad2(dt.getSeconds());

    return dtstring;
  }

  static toSqlDateFormat(date: Date): string {
    var dt = date;
    var dtstring =
      dt.getFullYear() +
      "-" +
      this.pad2(dt.getMonth() + 1) +
      "-" +
      this.pad2(dt.getDate());

    return dtstring;
  }
}
