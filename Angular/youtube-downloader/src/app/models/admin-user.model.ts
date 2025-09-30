export interface AdminUserListItem {
  id: string;
  email: string;
  displayName: string;
  recognizedVideos: number;
  registeredAt: string;
  roles: string[];
  youtubeCaptionIps: string[];
}

export interface AdminUsersPage {
  items: AdminUserListItem[];
  totalCount: number;
}
