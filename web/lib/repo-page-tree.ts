import type * as PageTree from "fumadocs-core/page-tree";
import type { RepoTreeNode } from "@/types/repository";
import { buildRepoDocPath } from "@/lib/repo-route";

function normalizeQueryString(queryString: string): string {
  const raw = queryString.startsWith("?") ? queryString.slice(1) : queryString;

  if (!raw) {
    return "";
  }

  const params = Array.from(new URLSearchParams(raw).entries()).sort(([keyA, valueA], [keyB, valueB]) => {
    if (keyA === keyB) {
      return valueA.localeCompare(valueB);
    }

    return keyA.localeCompare(keyB);
  });

  return params
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join("&");
}

function hashString(value: string): string {
  let hash = 2166136261;

  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return (hash >>> 0).toString(36);
}

function createPageTreeId(owner: string, repo: string, normalizedQuery: string, nodes: RepoTreeNode[]): string {
  const signature = JSON.stringify(nodes);
  const queryPart = normalizedQuery || "default";

  return `repo-tree:${owner}:${repo}:${queryPart}:${hashString(signature)}`;
}

function convertRepoTreeNode(
  node: RepoTreeNode,
  owner: string,
  repo: string,
  normalizedQuery: string,
  nodeId: string,
): PageTree.Node {
  const baseUrl = buildRepoDocPath(owner, repo, node.slug);
  const url = normalizedQuery ? `${baseUrl}?${normalizedQuery}` : baseUrl;

  if (node.children.length > 0) {
    return {
      $id: nodeId,
      type: "folder",
      name: node.title,
      url,
      children: node.children.map((child, index) =>
        convertRepoTreeNode(child, owner, repo, normalizedQuery, `${nodeId}:${index}`),
      ),
    } as PageTree.Folder;
  }

  return {
    $id: nodeId,
    type: "page",
    name: node.title,
    url,
  } as PageTree.Item;
}

export function buildRepoPageTree(
  nodes: RepoTreeNode[],
  owner: string,
  repo: string,
  queryString: string,
): PageTree.Root {
  const normalizedQuery = normalizeQueryString(queryString);
  const treeId = createPageTreeId(owner, repo, normalizedQuery, nodes);

  return {
    $id: treeId,
    name: `${owner}/${repo}`,
    children: nodes.map((node, index) =>
      convertRepoTreeNode(node, owner, repo, normalizedQuery, `${treeId}:${index}`),
    ),
  };
}

export { normalizeQueryString };
