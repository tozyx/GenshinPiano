package genshin_piano;

import java.awt.Graphics;
import java.awt.Image;
import javax.swing.ImageIcon;
import javax.swing.JPanel;

public class backgroundPanel extends JPanel {
    ImageIcon icon;
    Image img;

    public backgroundPanel() {
        if (readFile.readProperty("componentImagePath") != null) {
            icon = new ImageIcon(readFile.readProperty("componentImagePath"));
            img = icon.getImage();
        }

    }

    public void paintComponent(Graphics g) {
        super.paintComponent(g);
        g.drawImage(img, 0, 0, this.getWidth(), this.getHeight(), this);
    }
}
