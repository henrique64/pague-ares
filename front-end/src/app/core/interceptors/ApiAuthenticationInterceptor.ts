import { HttpInterceptor, HttpHandler, HttpEvent, HttpRequest } from "@angular/common/http";
import { Injectable } from "@angular/core";
import { AuthService } from "app/services/auth.service";
import { from, Observable } from "rxjs";

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return from(this.handle(req, next))
  }

  async handle(request: HttpRequest<any>, next: HttpHandler) {
    let exceptionPaths = [
        "api/Auth",
        "assets/config.json"
    ]

    if(exceptionPaths.find(e => request.url.includes(e))) {
        return next.handle(request).toPromise();
    }

    const authToken = this.authService.getToken();

    if (authToken) {
      request = request.clone({
        setHeaders: {
          Authorization: `Bearer ${authToken}`
        }
      });
    }

    return next.handle(request).toPromise();
  }
}