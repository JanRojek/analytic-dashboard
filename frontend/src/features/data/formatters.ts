export const number = new Intl.NumberFormat("en", {
  maximumFractionDigits: 2,
});

export const date = (value: string) =>
  new Date(value).toLocaleDateString("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
