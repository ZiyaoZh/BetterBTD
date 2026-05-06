import ctypes
import os
from pathlib import Path
import tkinter as tk

from PIL import Image, ImageTk

def main():
    # 在Windows上禁用DPI缩放（解决高DPI屏幕问题）
    if os.name == 'nt':
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(2)
        except:
            pass
    
    # 创建主窗口
    root = tk.Tk()
    root.title("testWindow")
    
    # 设置窗口大小为 1920x1080
    root.geometry("1280x720")
    
    # 可选：设置窗口在屏幕中央
    root.update_idletasks()
    width = root.winfo_width()
    height = root.winfo_height()
    x = (root.winfo_screenwidth() // 2) - (width // 2)
    y = (root.winfo_screenheight() // 2) - (height // 2)
    root.geometry(f'+{x}+{y}')

    # 加载并显示同目录下的 test.jpg
    image_path = Path(__file__).with_name("test.jpg")
    image = Image.open(image_path)
    photo = ImageTk.PhotoImage(image)

    label = tk.Label(root, image=photo)
    label.image = photo
    label.pack(expand=True)
    
    # 运行主循环
    root.mainloop()

if __name__ == "__main__":
    main()
