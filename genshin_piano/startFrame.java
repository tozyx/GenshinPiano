package genshin_piano;

import java.awt.*;
import java.awt.event.*;
import java.io.File;
import java.io.IOException;
import java.net.URI;
import java.net.URISyntaxException;
import java.util.ArrayList;
import java.util.Objects;
import javax.swing.*;

import static java.awt.Toolkit.getDefaultToolkit;

public class startFrame {
    JButton timeYes = new JButton("确定");
    MenuItem set1time = new MenuItem("设置一拍时长");
    MenuItem componentframe = new MenuItem("编曲界面");
    MenuItem top = new MenuItem("置顶窗口");
    static int[] t = new int[1];
    static boolean i = true;//只显示写入一次readme
    ArrayList<Double> tolTime = new ArrayList<>();
    ArrayList<String> tolLyrics = new ArrayList<>();
    ArrayList<String> readList = new ArrayList<>();
    File file;
    final String[] imagePath = new String[1];
    static boolean[] isTop = new boolean[]{false};
    Frame startFrame = new Frame("Genshin-Piano                     by  冰焰_Frozen");
    JDialog setTimeFrame = new JDialog(startFrame, "设置一拍时长 100~2000", true);
    static JTextField textField = new JTextField();
    JTextField time = new JTextField();
    JDialog exitLog = new JDialog(startFrame, "是否直接退出", false);
    FileDialog imageOpening = new FileDialog(startFrame, "选择背景图片路径", 0);
    FileDialog pianoOpening = new FileDialog(startFrame, "选择弹奏文件", 0);
    Toolkit kit = getDefaultToolkit();
    Dimension windowSize = kit.getScreenSize();
    JButton yes1 = new JButton("是");
    JButton no1 = new JButton("否");
    static MenuItem startPlay = new MenuItem("开始弹奏");


    void setPic() {
        Image icon = getDefaultToolkit().getImage(getClass().getClassLoader().getResource("resource/xg.jpg"));
        startFrame.setIconImage(icon);
        if (imagePath[0] != null && !imagePath[0].equals("0")) {
            ImageIcon img = new ImageIcon(imagePath[0]);
            JLabel start = new JLabel(img);
            if (piano.isSetPic[0]) {
                // img.setImage(img.getImage().getScaledInstance(Integer.parseInt(genshin_piano.readFile.readProperty("startComponentWide")), Integer.parseInt(genshin_piano.readFile.readProperty("startComponentHeight")), 1));
                startFrame.add(start);
                startFrame.pack();
                if (readFile.readProperty("startComponentWide") != null) {
                    startFrame.setBounds((int) (windowSize.getWidth() / 2.0D - (double) (start.getWidth()) / 2), (int) (windowSize.getHeight() / 2.0D - (double) (start.getHeight() / 2)), start.getWidth(), start.getHeight());
                }
                piano.isSetPic[0] = false;
            } else {
                startFrame.add(start);
                startFrame.pack();
                if (readFile.readProperty("startComponentWide") != null) {
                    startFrame.setBounds((int) (windowSize.getWidth() / 2.0D - (double) (Integer.parseInt(readFile.readProperty("startComponentWide")) / 2)), (int) (windowSize.getHeight() / 2.0D - (double) (Integer.parseInt(readFile.readProperty("startComponentHeight")) / 2)), Integer.parseInt(readFile.readProperty("startComponentWide")), Integer.parseInt(readFile.readProperty("startComponentHeight")));
                }
            }
        } else {
            if (readFile.readProperty("startComponentWide") != null) {
                startFrame.setBounds((int) (windowSize.getWidth() / 2.0D - (double) (Integer.parseInt(readFile.readProperty("startComponentWide")) / 2)), (int) (windowSize.getHeight() / 2.0D - (double) (Integer.parseInt(readFile.readProperty("startComponentHeight")) / 2)), Integer.parseInt(readFile.readProperty("startComponentWide")), Integer.parseInt(readFile.readProperty("startComponentHeight")));
            } else
                startFrame.setBounds((int) windowSize.getWidth() / 2 - 325, (int) windowSize.getHeight() / 2 - 40, 650, 80);
        }

    }

    private void getImagePath() {
        imageOpening.setFile("*.jpg;*.png;*.gif");
        imageOpening.setVisible(true);
        startFrame.setAlwaysOnTop(false);
        top.setLabel("置顶窗口");
        isTop[0] = false;
        imageOpening.setAlwaysOnTop(true);
        if (imageOpening.getFile() != null) {
            imagePath[0] = imageOpening.getDirectory() + imageOpening.getFile();
            writeFile.writeProperty("imagePath", imagePath[0]);
            System.out.println("图片路径:" + imagePath[0]);
        } else {
            imagePath[0] = "0";
            System.out.println("读取图片失败");
            textField.setText("读取图片失败");
        }

    }

    private void openGenshin() {
        String[] opendi = new String[]{null};
        String[] openfile = new String[]{null};
        pianoOpening.setFile("*.GenshinPiano");
        pianoOpening.setAlwaysOnTop(true);
        startFrame.setAlwaysOnTop(false);
        top.setLabel("置顶窗口");
        isTop[0] = false;
//        pianoOpening.setDirectory((new getJarPath()).getJarPath());
//        System.out.println(pianoOpening.getDirectory());
        pianoOpening.setVisible(true);
        if (pianoOpening.getFile() != null) {
            opendi[0] = pianoOpening.getDirectory();
            openfile[0] = pianoOpening.getFile();
            System.out.println("打开文件路径：" + opendi[0] + openfile[0]);

            try {
                readFile.read(readList, opendi[0], openfile[0]);
                pianoOpening.setFile(null);
                pianoOpening.setVisible(false);
                System.out.println("打开文件成功");
                String[] a = openfile[0].split("\\.");
                textField.setText("打开文件成功  " + a[0]);
            } catch (IOException e) {
                e.printStackTrace();
            }
        }

    }

    void startFrameInitialize() {
        startFrame.removeAll();
        readList.clear();
        tolLyrics.clear();
        startFrame.setLayout(new BorderLayout());
        textField.setBorder(BorderFactory.createEmptyBorder());
        textField.setFocusable(false);
        textField.setEditable(false);
        textField.setOpaque(false);
        textField.setFont(new Font("黑体", Font.BOLD | Font.ITALIC, 15));
        startFrame.add(textField, BorderLayout.NORTH);
        final boolean[] canExit = new boolean[]{true};
        file = new File((new getJarPath()).getJarPath());
        piano.properties = new File(file + "\\config.properties");
        if (readFile.readProperty("1time") == null || Integer.parseInt(readFile.readProperty("1time")) < 50 || Integer.parseInt(readFile.readProperty("1time")) > 2000) {
            writeFile.writeProperty("1time", "180");
        }
        t[0] = Integer.parseInt(readFile.readProperty("1time"));
        if ((new File(file + "\\config.properties")).exists()) {
            imagePath[0] = readFile.readProperty("imagePath");
        } else {
            writeFile.writeProperty("write_config", "true");
        }

        setPic();
        new readMe(file);
        MenuBar bar = new MenuBar();
        Menu menu = new Menu("菜单");
        Menu menufile = new Menu("文件");
        Menu set = new Menu("个性化");
        Menu color = new Menu("背景");
        Menu font = new Menu("菜单字体");
        Menu play = new Menu("弹奏");
        Menu auth = new Menu("联系作者");
        MenuItem open = new MenuItem("打开弹奏文件");
        MenuItem image = new MenuItem("本地图片..");
        MenuItem design = new MenuItem("系统默认");
        final MenuItem stopPlay = new MenuItem("停止弹奏", new MenuShortcut(80, false));
        MenuItem auB = new MenuItem("B站主页");
        MenuItem auA = new MenuItem("爱发电主页");
        setTimeFrame.setLayout(new GridLayout(2, 2, 2, 0));
        JButton danWei = new JButton("ms");
        danWei.setEnabled(false);
        danWei.setBorder(BorderFactory.createEmptyBorder());
        danWei.setContentAreaFilled(false);
        timeYes.setBorder(BorderFactory.createEmptyBorder());
        timeYes.setContentAreaFilled(false);
        timeYes.setFont(new Font("黑体", Font.PLAIN, 12));
        JButton timeNo = new JButton("取消");
        timeNo.setBorder(BorderFactory.createEmptyBorder());
        time.setOpaque(false);
        time.setBorder(BorderFactory.createEmptyBorder());
        timeNo.setContentAreaFilled(false);
        timeNo.setFont(new Font("黑体", Font.PLAIN, 12));
        setTimeFrame.add(time);
        setTimeFrame.add(danWei);
        setTimeFrame.add(timeYes);
        setTimeFrame.add(timeNo);

//        startFrame.addWindowFocusListener(new WindowAdapter() {
//            @Override
//            public void windowLostFocus(WindowEvent e) {
//                if (readList.size()!=0&&!PressNew.isPlaying){
//                    t[0] = Integer.parseInt(readFile.readProperty("1time"));
//                    Thread t1 = new Thread(new PressNew.MyTread(tolLyrics, tolTime), "T1");
//                    //t1.setPriority(10); 线程优先级
//                    PressNew.isPlaying = true;
//                    startPlay.setEnabled(false);
//                    set1time.setEnabled(false);
//                    componentframe.setEnabled(false);
//                    System.out.println("一拍时长：" + t[0] + "  ms");
//                    t1.start();
//                }
//            }
//        });
        startFrame.addWindowFocusListener(new WindowAdapter() {
            @Override
            public void windowGainedFocus(WindowEvent e) {
                PressNew.isPlaying = false;
                set1time.setEnabled(true);
                componentframe.setEnabled(true);
            }
        });
        time.addKeyListener(new KeyListener() {
            public void keyTyped(KeyEvent e) {
            }

            public void keyPressed(KeyEvent e) {
            }

            public void keyReleased(KeyEvent e) {
                if (e.getKeyCode() == KeyEvent.VK_ENTER) {
                    set1Time();
                }

            }
        });
        timeYes.addActionListener(new AbstractAction() {
            public void actionPerformed(ActionEvent e) {
                timeYes.setEnabled(false);
                set1Time();
                timeYes.setEnabled(true);
            }
        });
        timeNo.addActionListener(new AbstractAction() {
            public void actionPerformed(ActionEvent e) {
                setTimeFrame.setVisible(false);
            }
        });
        setTimeFrame.addWindowListener(new WindowAdapter() {
            public void windowClosing(WindowEvent e) {
                setTimeFrame.setVisible(false);
            }
        });
        menu.add(new MenuItem("-"));
        menu.add(componentframe);
        menu.add(top);
        menufile.add(open);
        color.add(image);
        color.add(design);
        set.add(new MenuItem("-"));
        set.add(color);
        set.add(font);
        play.add(startPlay);
        play.add(stopPlay);
        play.add(set1time);
        startPlay.setEnabled(false);
        stopPlay.setEnabled(false);
        auth.add(auB);
        auth.add(auA);
        bar.add(menu);
        bar.add(set);
        bar.add(menufile);
        bar.add(play);
        bar.add(auth);
        startFrame.setMenuBar(bar);
        //菜单监听
        {
            time.addMouseWheelListener(new MouseAdapter() {
                @Override
                public void mouseWheelMoved(MouseWheelEvent e) {
                    if (!new componentFrame().isNumeric(time.getText()))
                        time.setText("150");
                    if (Integer.parseInt(time.getText().trim()) + 1 <= 2000 && e.getWheelRotation() == -1)
                        time.setText(String.valueOf(Integer.parseInt(time.getText().trim()) + 1));
                    else if (Integer.parseInt(time.getText().trim()) - 1 >= 100 && e.getWheelRotation() == 1)
                        time.setText(String.valueOf(Integer.parseInt(time.getText().trim()) - 1));
                }
            });
            set1time.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    time.setText(readFile.readProperty("1time"));
                    dialogCenter(setTimeFrame, 200, 82);
                }
            });
            auB.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    try {
                        URI uri = null;
                        try {
                            uri = new URI("https://space.bilibili.com/507499226");
                        } catch (URISyntaxException var4) {
                            var4.printStackTrace();
                        }
                        Desktop.getDesktop().browse(uri);
                    } catch (IOException var5) {
                        var5.printStackTrace();
                    }

                }
            });
            auA.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    try {
                        URI uri = null;
                        try {
                            uri = new URI("https://afdian.net/@Frozen_Flame");
                        } catch (URISyntaxException var4) {
                            var4.printStackTrace();
                        }

                        Desktop.getDesktop().browse(uri);
                    } catch (IOException var5) {
                        var5.printStackTrace();
                    }

                }
            });
            design.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    writeFile.writeProperty("imagePath", "");
                    writeFile.writeProperty("startComponentWide", String.valueOf(startFrame.getWidth()));
                    writeFile.writeProperty("startComponentHeight", String.valueOf(startFrame.getHeight()));
                    refresh();
                }
            });
            image.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    getImagePath();
                    piano.isSetPic[0] = true;
                    refresh();
                }
            });
            stopPlay.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    if (PressNew.isPlaying)
                        System.out.println("已停止弹奏");
                    PressNew.isPlaying = false;
                    set1time.setEnabled(true);
                    startPlay.setEnabled(true);
                    componentframe.setEnabled(true);
                }
            });
            startPlay.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    t[0] = Integer.parseInt(readFile.readProperty("1time"));
                    Thread t1 = new Thread(new PressNew.MyTread(tolLyrics, tolTime), "T1");
                    //t1.setPriority(10); 线程优先级
                    PressNew.isPlaying = true;
                    startPlay.setEnabled(false);
                    set1time.setEnabled(false);
                    componentframe.setEnabled(false);
                    System.out.println("一拍时长：" + t[0] + "  ms");
                    for (int i = 5; i > 0; i--) {
                        System.out.println(i + "秒后开始弹奏");
                        try {
                            Thread.sleep(1000);
                        } catch (InterruptedException ex) {
                            System.out.println("开始弹奏延时错误");
                        }
                    }

                    t1.start();
                }
            });
            open.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    tolLyrics.clear();
                    readList.clear();
                    top.setLabel("窗口置顶");
                    startFrame.setAlwaysOnTop(false);
                    canExit[0] = false;
                    openGenshin();
                    if (readList.size() != 0) {
                        startPlay.setEnabled(true);
                        stopPlay.setEnabled(true);
                        Coder.separate(readList, tolLyrics, tolTime);
                    }

                }
            });
            top.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    isTop[0] = !isTop[0];
                    if (isTop[0]) {
                        startFrame.setAlwaysOnTop(true);
                        top.setLabel("窗口取消置顶");
                    } else {
                        top.setLabel("窗口置顶");
                        startFrame.setAlwaysOnTop(false);
                    }

                }
            });
            yes1.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    System.exit(0);
                }
            });
            no1.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    exitLog.setVisible(false);
                }
            });
        }
        exitLog.setLayout(new GridLayout(1, 2, 0, 0));
        yes1.setBorder(BorderFactory.createEmptyBorder());
        no1.setBorder(BorderFactory.createEmptyBorder());
        yes1.setFocusable(false);
        no1.setFocusable(false);
        yes1.setBackground(Color.white);
        no1.setBackground(Color.white);
        yes1.setOpaque(false);
        no1.setOpaque(false);
        exitLog.add(yes1);
        exitLog.add(no1);
        startFrame.addWindowListener(new WindowAdapter() {
            public void windowClosing(WindowEvent e) {
                if (canExit[0]) {
                    System.exit(0);
                } else {
                    if (exitLog.isVisible()) {
                        System.exit(0);
                    } else {
                        dialogCenter(exitLog, 200, 60);
                        exitLog.setAlwaysOnTop(true);
                    }
                }

            }
        });
        componentframe.addActionListener(new AbstractAction() {
            public void actionPerformed(ActionEvent e) {
                writeFile.writeProperty("startComponentWide", String.valueOf(startFrame.getWidth()));
                writeFile.writeProperty("startComponentHeight", String.valueOf(startFrame.getHeight()));
                componentFrame componentFrame = new componentFrame();
                componentFrame.componentInitialize();
                startFrame.dispose();
            }
        });
    }

    void set1Time() {
        if (!Objects.equals(time.getText(), "设置成功") && !Objects.equals(time.getText(), "非法输入") && !Objects.equals(time.getText(), "")) {
            if (Integer.parseInt(time.getText().trim()) >= 100 && Integer.parseInt(time.getText().trim()) <= 2000) {
                writeFile.writeProperty("1time", String.valueOf(Integer.parseInt(time.getText().trim())));
                textField.setText("一拍时长：" + readFile.readProperty("1time") + "  ms");
                setTimeFrame.setVisible(false);
            } else {
                time.setText("非法输入");
                time.setEditable(false);

                try {
                    Thread.sleep(1000L);
                } catch (InterruptedException var2) {
                    var2.printStackTrace();
                }
                time.setText("");
                time.setEditable(true);
            }

        }

    }

    //刷新
    private void refresh() {
        int x = startFrame.getLocationOnScreen().x;
        int y = startFrame.getLocationOnScreen().y;
        startFrame.dispose();
        startFrame layoutgui = new startFrame();
        layoutgui.showStartFrame();
        layoutgui.startFrame.setLocation(x, y);
    }

    public void dialogCenter(JDialog j, int dialogX, int dialogY) {
        Point locationOnScreen = startFrame.getLocationOnScreen();
        Dimension componentSize = startFrame.getSize();
        j.setBounds(locationOnScreen.x + componentSize.width / 2 - dialogX / 2, locationOnScreen.y + componentSize.height / 2 - dialogY / 2, dialogX, dialogY);
        j.setVisible(true);
        j.setResizable(false);
    }

    public void showStartFrame() {
        startFrameInitialize();
        startFrame.setVisible(true);
    }

}
