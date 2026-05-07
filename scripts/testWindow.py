import ctypes
import os
from pathlib import Path
import sys
import tkinter as tk

from PIL import Image, ImageTk

def main(resolution: str = "720p"):
    # 在Windows上禁用DPI缩放（解决高DPI屏幕问题）
    if os.name == 'nt':
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(2)
        except:
            pass

    if resolution not in ("720p", "1080p"):
        raise ValueError("resolution 仅支持 720p 或 1080p")

    if resolution == "720p":
        window_size = "1280x720"
        image_range = range(3)
    else:
        window_size = "1920x1080"
        image_range = range(3, 6)
    
    # 创建主窗口
    root = tk.Tk()
    root.title("testWindow")
    
    # 根据分辨率参数设置窗口大小
    root.geometry(window_size)
    
    # 可选：设置窗口在屏幕中央
    root.update_idletasks()
    width = root.winfo_width()
    height = root.winfo_height()
    x = (root.winfo_screenwidth() // 2) - (width // 2)
    y = (root.winfo_screenheight() // 2) - (height // 2)
    root.geometry(f'+{x}+{y}')

    # 根据分辨率参数加载对应图片并轮播（每 2 秒切换）
    image_paths = [Path(__file__).with_name(f"test{i}.jpg") for i in image_range]
    photos = [ImageTk.PhotoImage(Image.open(path)) for path in image_paths]

    label = tk.Label(root, image=photos[0])
    label.image = photos[0]
    label.pack(expand=True)

    current_index = 0

    def update_image():
        nonlocal current_index
        current_index = (current_index + 1) % len(photos)
        label.configure(image=photos[current_index])
        label.image = photos[current_index]
        root.after(2000, update_image)

    root.after(2000, update_image)
    
    # 运行主循环
    root.mainloop()

if __name__ == "__main__":
    resolution_arg = sys.argv[1] if len(sys.argv) > 1 else "720p"
    main(resolution_arg)
