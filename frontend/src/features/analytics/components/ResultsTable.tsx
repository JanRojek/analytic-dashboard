import type { QueryConfig, QueryItem } from "../../../data/types";
import { formatName, queryCaption } from "../chartConfig";

export function ResultsTable({
  items,
  config,
}: {
  items: QueryItem[];
  config: QueryConfig;
}) {
  return (
    <div className="results-table">
      <table>
        <caption className="sr-only">
          Query results: {queryCaption(config)}
        </caption>
        <thead>
          <tr>
            <th scope="col">{formatName(config.groupByColumn)}</th>
            <th scope="col">
              {config.aggregation} · {formatName(config.measureColumn)}
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((item, index) => (
            <tr key={index}>
              <td>{item.label ?? <span className="muted">(empty)</span>}</td>
              <td>
                {item.value.toLocaleString("en", {
                  maximumFractionDigits: 2,
                })}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
