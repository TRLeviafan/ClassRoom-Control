using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using ClassRoom_Control.Models;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Teacher;

public class StudentManager
{
    public ObservableCollection<StudentInfo> Students { get; } = new();

    public event Action? StudentsChanged;

    public void RegisterStudent(string id, string name, string ip)
    {
        RunOnUi(() =>
        {
            var existing = Students.FirstOrDefault(s => s.Id == id || (s.IpAddress == ip && s.Name == name));
            if (existing != null)
            {
                existing.Id = id;
                existing.Name = name;
                existing.IpAddress = ip;
                existing.IsOnline = true;
                existing.LastSeen = DateTime.UtcNow;
            }
            else
            {
                var newStudent = new StudentInfo
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? $"ПК-{Students.Count + 1:D2}" : name,
                    IpAddress = ip,
                    IsOnline = true,
                    LastSeen = DateTime.UtcNow
                };
                Students.Add(newStudent);
            }
            StudentsChanged?.Invoke();
        });
    }

    public void UpdateHeartbeat(string id, bool isLocked, bool isDemoActive)
    {
        RunOnUi(() =>
        {
            var student = Students.FirstOrDefault(s => s.Id == id);
            if (student != null)
            {
                student.IsOnline = true;
                student.IsLocked = isLocked;
                student.IsDemoActive = isDemoActive;
                student.LastSeen = DateTime.UtcNow;
                StudentsChanged?.Invoke();
            }
        });
    }

    public void MarkOffline(string id)
    {
        RunOnUi(() =>
        {
            var student = Students.FirstOrDefault(s => s.Id == id);
            if (student != null)
            {
                student.IsOnline = false;
                StudentsChanged?.Invoke();
            }
        });
    }

    public void CheckStaleStudents()
    {
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(NetworkConstants.DisconnectTimeoutSeconds);

        RunOnUi(() =>
        {
            bool changed = false;
            foreach (var student in Students)
            {
                if (student.IsOnline && (now - student.LastSeen) > timeout)
                {
                    student.IsOnline = false;
                    changed = true;
                }
            }
            if (changed) StudentsChanged?.Invoke();
        });
    }

    public void SetAllDemoState(bool isActive)
    {
        RunOnUi(() =>
        {
            foreach (var s in Students.Where(s => s.IsOnline))
            {
                s.IsDemoActive = isActive;
            }
            StudentsChanged?.Invoke();
        });
    }

    public void SetAllLockState(bool isLocked)
    {
        RunOnUi(() =>
        {
            foreach (var s in Students.Where(s => s.IsOnline))
            {
                s.IsLocked = isLocked;
            }
            StudentsChanged?.Invoke();
        });
    }

    public void SetStudentLock(string id, bool isLocked)
    {
        RunOnUi(() =>
        {
            var student = Students.FirstOrDefault(s => s.Id == id);
            if (student != null)
            {
                student.IsLocked = isLocked;
                StudentsChanged?.Invoke();
            }
        });
    }

    public void UpdateThumbnail(string id, string base64)
    {
        if (string.IsNullOrEmpty(base64)) return;

        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            RunOnUi(() =>
            {
                var student = Students.FirstOrDefault(s => s.Id == id);
                if (student != null)
                {
                    using var ms = new System.IO.MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    student.Thumbnail = bmp;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update student thumbnail: {ex.Message}");
        }
    }

    private void RunOnUi(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}