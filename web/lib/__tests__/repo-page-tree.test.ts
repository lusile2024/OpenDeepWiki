import { describe, expect, it } from "vitest";
import type { RepoTreeNode } from "@/types/repository";
import { buildRepoPageTree, normalizeQueryString } from "@/lib/repo-page-tree";

const baseNodes: RepoTreeNode[] = [
  {
    title: "核心业务流程",
    slug: "business-workflows",
    children: [
      {
        title: "任务执行反馈",
        slug: "business-workflows/wcs-report",
        children: [],
      },
      {
        title: "处理站台入库申请",
        slug: "business-workflows/wcs-stn-move-in",
        children: [],
      },
    ],
  },
];

describe("buildRepoPageTree", () => {
  it("对同一棵文档树生成稳定且可复用的根 id", () => {
    const first = buildRepoPageTree(baseNodes, "local", "WmsServerV4Dev", "lang=zh-CN&branch=main");
    const second = buildRepoPageTree(baseNodes, "local", "WmsServerV4Dev", "branch=main&lang=zh-CN");

    expect(first.$id).toBe(second.$id);
    expect(first.children[0]?.$id).toBe(second.children[0]?.$id);
  });

  it("对不同文档树生成不同的根 id，避免 footer 缓存串用", () => {
    const changedNodes: RepoTreeNode[] = [
      {
        ...baseNodes[0],
        children: [
          {
            title: "发送请求到wcs",
            slug: "business-workflows/send",
            children: [],
          },
          ...baseNodes[0].children,
        ],
      },
    ];

    const first = buildRepoPageTree(baseNodes, "local", "WmsServerV4Dev", "branch=main&lang=zh-CN");
    const second = buildRepoPageTree(changedNodes, "local", "WmsServerV4Dev", "branch=main&lang=zh-CN");

    expect(first.$id).not.toBe(second.$id);
  });

  it("规范化查询字符串，保证页面链接稳定", () => {
    expect(normalizeQueryString("lang=zh-CN&branch=main")).toBe("branch=main&lang=zh-CN");

    const tree = buildRepoPageTree(baseNodes, "local", "WmsServerV4Dev", "lang=zh-CN&branch=main");
    const folder = tree.children[0];

    expect(folder?.type).toBe("folder");
    if (!folder || folder.type !== "folder") {
      throw new Error("expected first node to be a folder");
    }

    const firstPage = folder.children[0];
    expect(firstPage?.type).toBe("page");
    if (!firstPage || firstPage.type !== "page") {
      throw new Error("expected first child to be a page");
    }

    expect(firstPage.url).toBe("/local/WmsServerV4Dev/business-workflows/wcs-report?branch=main&lang=zh-CN");
  });
});
