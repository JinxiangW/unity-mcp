export function assertPathOrGuid(args) {
  if (!args.path && !args.guid) {
    throw new Error("Either 'path' or 'guid' is required.");
  }
}

export function normalizeMaterialSearchResult(result) {
  if (!result?.data?.materials) {
    return result;
  }

  const materials = result.data.materials.filter((entry) =>
    typeof entry?.path === "string" && entry.path.toLowerCase().endsWith(".mat"),
  );

  return {
    ...result,
    data: {
      ...result.data,
      materials,
      materialCount: materials.length,
    },
  };
}

export function normalizeFindAssetsResult(result, args) {
  if (args?.type !== "Material" || !result?.data?.assets) {
    return result;
  }

  const assets = result.data.assets.filter((entry) =>
    typeof entry?.path === "string" && entry.path.toLowerCase().endsWith(".mat"),
  );

  return {
    ...result,
    data: {
      ...result.data,
      returned: assets.length,
      assets,
    },
  };
}
