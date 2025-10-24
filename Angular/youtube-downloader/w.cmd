# 0) �������, ��� � ���� ������ �� ��������� ���������, ������� ������ ������� main
git status
# ���� ���� ����������������� ������ � ���������� (��� stashe�)
git add -A
git commit -m "Make current local state the new main"  # ��� �������������

# 1) ��������� ������ � �������
git fetch origin

# 2) ��������� ������� origin/main � ��������� ����� �� ������� (backup)
git push origin origin/main:refs/heads/old-main
# ��� ������� ��������� ������ ���, ��������: main-backup-2025-10-24

# 3) ���������, ��� �������� �� �� ������ �����.
# ���� �� ��� �� main � ��� ������ ��������� � ��.
# ���� �� �� ������ ����� � ������ ���������� � ���������� � � main:
# git branch -M main

# 4) ������������ ��������� main ����� ��������� ���������� (� ������� �� ������ ���������)
git push --force-with-lease origin main:main
