/**
 * Aperture patches for react-grid-layout 2.3.0:
 * 1. Resize preview follows accepted grid dimensions (no overlap with neighbors).
 * 2. During drag, the visual widget stays inside horizontal canvas bounds;
 *    vertical movement remains unbounded to preserve native auto-scroll.
 * 3. Visual drag bounds align with grid container padding on both sides.
 * 3a. CJS and ESM use their respective utility import names.
 * 4. At the outermost grid columns, visual drag snaps to the same edge as
 *    the placeholder. Interior dragging remains pixel-smooth.
 * 5. Dragging cannot move the visible widget above the first grid row;
 *    downward movement remains unbounded for native auto-scroll.
 * 6. When preview snaps to row 0, render the same top for the widget.
 *
 * Runs automatically through the existing postinstall script.
 * Idempotent; verifies both RGL bundles before writing and refuses unknown versions.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const packageRoot = resolve(frontendRoot, 'node_modules/react-grid-layout');
const packageInfo = JSON.parse(
  await readFile(resolve(packageRoot, 'package.json'), 'utf8'),
);

if (packageInfo.version !== '2.3.0') {
  throw new Error(
    `Aperture patch supports RGL 2.3.0; found ${packageInfo.version}. Review the patch before upgrading.`,
  );
}

const bundleNames = ['chunk-CUP5Y2ZG.js', 'chunk-Z6CWRR74.mjs'];

const resizeOriginal = /(dragging\s*\?\s*dragPositionRef\.current\s*:\s*null\s*,\s*)(resizing\s*\?\s*resizePositionRef\.current\s*:\s*null)/g;
const resizePatched = /dragging\s*\?\s*dragPositionRef\.current\s*:\s*null\s*,\s*null\s*\)/g;

// Insert the horizontal clamp immediately after the live pixel drag coordinates
// are advanced. Applying grid constraints alone would not clamp this preview.
const dragAnchor = /let\s+top\s*=\s*dragPositionRef\.current\.top\s*\+\s*deltaY\s*;\s*let\s+left\s*=\s*dragPositionRef\.current\.left\s*\+\s*deltaX\s*;/g;
const dragMarker = '/* Aperture: horizontal-only visual drag bounds */';
// Migrate the old horizontal clamp if this script is run against an already-patched RGL.
const oldHorizontalClamp = `\n  ${dragMarker}\n  left = clamp(\n    left,\n    0,\n    Math.max(\n      0,\n      containerWidth - calcGridItemWHPx(w, calcGridColWidth(positionParams), margin[0])\n    )\n  );`;
// GridLayout uses containerPadding as an inset on both sides. Align the live
// dragging widget with the left/right edge positions of grid placeholders.
const horizontalClampUnqualified = `\n  ${dragMarker}\n  const apertureHorizontalPadding = positionParams.containerPadding[0];\n  const apertureWidgetWidth = calcGridItemWHPx(w, calcGridColWidth(positionParams), margin[0]);\n  left = clamp(\n    left,\n    apertureHorizontalPadding,\n    Math.max(apertureHorizontalPadding, containerWidth - apertureHorizontalPadding - apertureWidgetWidth)\n  );`;
// CJS calls the utility functions through its generated module namespace;
// the ESM bundle imports those functions directly.
const horizontalClamp = (isEsm) => isEsm ? horizontalClampUnqualified :
  horizontalClampUnqualified
    .replace('calcGridItemWHPx(w, calcGridColWidth(positionParams), margin[0])',
      'chunkVJJIA3HC_js.calcGridItemWHPx(w, chunkVJJIA3HC_js.calcGridColWidth(positionParams), margin[0])')
    .replace('left = clamp(', 'left = chunkVJJIA3HC_js.clamp(');

// The grid placeholder snaps to a column before the pixel-drag position reaches
// that column's exact left edge. At x=0 or x=cols-w, align just the rendered
// drag position with the snapped cell. Crucially, do NOT mutate the underlying
// dragPositionRef: that would cause cursor offset drift on the next mousemove.
const edgePreviewMarker = '/* Aperture: visual edge follows grid placeholder */';
const edgePreviewAnchor = /const\s+child\s*=\s*(?:React2__default\.default|React2)\.Children\.only\(children\)\s*;/g;
const edgePreviewPatchV1 = (calcRaw) => `  ${edgePreviewMarker}
  if (dragging) {
    const apertureGridX = ${calcRaw}(
      positionParams,
      dragPositionRef.current.top,
      dragPositionRef.current.left
    ).x;
    if (apertureGridX <= 0) {
      pos.left = positionParams.containerPadding[0];
    } else if (apertureGridX >= positionParams.cols - w) {
      pos.left = containerWidth - positionParams.containerPadding[0] - pos.width;
    }
  }
`;

// Use the same rule for the first row as for the outer columns.
// The drag ref is clamped during mouse movement, but the rendered frame can
// lag behind the snapped RGL placeholder when the grid row stops changing.
// This corrects only the rendered top at row zero, leaving the drag ref,
// collisions, resize, and downward scrolling untouched.
const edgePreviewPatch = (calcRaw) => `  ${edgePreviewMarker}
  if (dragging) {
    const apertureGridPosition = ${calcRaw}(
      positionParams,
      dragPositionRef.current.top,
      dragPositionRef.current.left
    );
    if (apertureGridPosition.x <= 0) {
      pos.left = positionParams.containerPadding[0];
    } else if (apertureGridPosition.x >= positionParams.cols - w) {
      pos.left = containerWidth - positionParams.containerPadding[0] - pos.width;
    }
    if (apertureGridPosition.y <= 0) {
      pos.top = positionParams.containerPadding[1];
    }
  }
`;

// Clamp only the TOP of an in-progress drag to the first grid row.
// Do not bound the bottom: RGL can extend its height while dragging downward.
// This acts on the same pixel position used to update dragPositionRef, keeping
// the visible widget and grid coordinates consistent without faking mousemoves.
const topBoundMarker = '/* Aperture: top-only visual drag bound */';
const topBoundPatch = `\n  ${topBoundMarker}\n  top = Math.max(positionParams.containerPadding[1], top);`;

const planned = [];
for (const name of bundleNames) {
  const path = resolve(packageRoot, 'dist', name);
  let source = await readFile(path, 'utf8');
  const originalCount = [...source.matchAll(resizeOriginal)].length;
  const patchedCount = [...source.matchAll(resizePatched)].length;

  if (originalCount === 1 && patchedCount === 0) {
    source = source.replace(resizeOriginal, '$1null');
  } else if (!(originalCount === 0 && patchedCount === 1)) {
    throw new Error(`Unexpected resize source in ${name}. Nothing was written.`);
  }

  const anchorMatches = [...source.matchAll(dragAnchor)];
  const markerCount = source.split(dragMarker).length - 1;
  if (anchorMatches.length !== 1 || markerCount > 1) {
    throw new Error(`Unexpected drag source in ${name}. Nothing was written.`);
  }
  const wantedHorizontalClamp = horizontalClamp(name.endsWith('.mjs'));
  if (markerCount === 0) {
    source = source.replace(dragAnchor, (match) => match + wantedHorizontalClamp);
  } else if (source.includes(oldHorizontalClamp)) {
    source = source.replace(oldHorizontalClamp, wantedHorizontalClamp);
  } else if (source.includes(horizontalClampUnqualified) && !name.endsWith('.mjs')) {
    // Upgrade the earlier Aperture patch (which used ESM names in the CJS build).
    source = source.replace(horizontalClampUnqualified, wantedHorizontalClamp);
  } else if (!source.includes(wantedHorizontalClamp)) {
    throw new Error(`Unexpected horizontal bounds patch in ${name}. Nothing was written.`);
  }

  // Works on both the existing Aperture-patched RGL and a fresh installation.
  // Only the active drag position is bounded at the top; resize is untouched.
  const topBoundCount = source.split(topBoundMarker).length - 1;
  if (topBoundCount === 0) {
    source = source.replace(dragAnchor, (match) => match + topBoundPatch);
  } else if (topBoundCount !== 1 || !source.includes(topBoundPatch)) {
    throw new Error(`Unexpected top bound patch in ${name}. Nothing was written.`);
  }

  const anchorCount = [...source.matchAll(edgePreviewAnchor)].length;
  const edgePreviewCount = source.split(edgePreviewMarker).length - 1;
  if (anchorCount !== 1 || edgePreviewCount > 1) {
    throw new Error(`Unexpected GridItem render source in ${name}. Nothing was written.`);
  }
  const calcRaw = name.endsWith('.mjs')
    ? 'calcXYRaw'
    : 'chunkVJJIA3HC_js.calcXYRaw';
  const previewCode = edgePreviewPatch(calcRaw);
  const previousPreviewCode = edgePreviewPatchV1(calcRaw);
  if (edgePreviewCount === 0) {
    source = source.replace(edgePreviewAnchor, (match) => previewCode + match);
  } else if (source.includes(previousPreviewCode)) {
    source = source.replace(previousPreviewCode, previewCode);
  } else if (!source.includes(previewCode)) {
    throw new Error(`Unexpected edge preview patch in ${name}. Nothing was written.`);
  }

  const existing = await readFile(path, 'utf8');
  planned.push({ name, path, source, changed: existing !== source });
}

// Validate all builds before modifying either one.
for (const { name, path, source, changed } of planned) {
  if (changed) {
    await writeFile(path, source, 'utf8');
    console.log(`[Aperture/RGL] Patched ${name}`);
  } else {
    console.log(`[Aperture/RGL] Already patched: ${name}`);
  }
}
