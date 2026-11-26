import type { PaginationParams } from "./types.ts";

export const DEFAULT_PAGE_SIZE = 24;
export const MAX_PAGE_SIZE = 96;

export function normalizePagination({
  page = 1,
  pageSize = DEFAULT_PAGE_SIZE,
}: PaginationParams = {}): Required<PaginationParams> {
  return {
    page:
      Number.isInteger(page) && page > 0 && page <= 2_147_483_647 ? page : 1,
    pageSize:
      Number.isInteger(pageSize) && pageSize > 0 && pageSize <= MAX_PAGE_SIZE
        ? pageSize
        : DEFAULT_PAGE_SIZE,
  };
}
