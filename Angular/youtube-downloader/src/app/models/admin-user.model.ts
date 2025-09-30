export interface AdminUserListItem {
  id: string;
  email: string;
  displayName: string;
  recognizedVideos: number;
  registeredAt: string;
  roles: string[];
}

export interface AdminUsersPage {
  items: AdminUserListItem[];
  totalCount: number;
}
