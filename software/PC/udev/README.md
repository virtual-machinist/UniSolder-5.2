# Linux USB Permissions (udev rules)

To communicate with the UniSolder 5.2 hardware over USB HID on Linux without requiring root / `sudo` privileges:

### 1. Copy the udev rule
```bash
sudo cp 99-unisolder.rules /etc/udev/rules.d/
```

### 2. Reload udev rules
```bash
sudo udevadm control --reload-rules
sudo udevadm trigger
```

### 3. Ensure user is in the `plugdev` group (if applicable)
```bash
sudo usermod -a -G plugdev $USER
```
*(Log out and log back in for group membership changes to take effect).*
