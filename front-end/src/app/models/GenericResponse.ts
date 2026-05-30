export interface GenericResponse<T> {
    success: boolean;
    message: string;
    data: T | null;
    records: number;
    pages: number;
    page: number;
}