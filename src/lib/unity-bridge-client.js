export class UnityBridgeClient {
  constructor(baseUrl, timeoutMs) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
    this.timeoutMs = timeoutMs;
  }

  async get(path, params = {}) {
    const url = new URL(`${this.baseUrl}${path}`);

    for (const [key, value] of Object.entries(params)) {
      if (value === undefined || value === null || value === "") {
        continue;
      }

      if (Array.isArray(value)) {
        url.searchParams.set(key, value.join(","));
        continue;
      }

      url.searchParams.set(key, String(value));
    }

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await fetch(url, {
        method: "GET",
        headers: { Accept: "application/json" },
        signal: controller.signal,
      });

      const text = await response.text();
      let parsed;

      try {
        parsed = text ? JSON.parse(text) : {};
      } catch {
        throw new Error(`Unity bridge returned non-JSON response (${response.status}): ${text}`);
      }

      if (!response.ok) {
        throw new Error(parsed?.error || `Unity bridge request failed with HTTP ${response.status}`);
      }

      if (parsed?.ok === false) {
        throw new Error(parsed?.error || "Unity bridge returned an error response");
      }

      return parsed;
    } catch (error) {
      if (error.name === "AbortError") {
        throw new Error(`Unity bridge request timed out after ${this.timeoutMs}ms`);
      }

      throw error;
    } finally {
      clearTimeout(timeout);
    }
  }
}
